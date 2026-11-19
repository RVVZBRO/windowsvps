package main

import (
	"bytes"
	"flag"
	"log"
	"net"
	"os"
	"os/signal"
	"sync"
	"syscall"
	"time"
)

const (
	magic             = "VZR1"
	msgHostRegister   = byte(1)
	msgJoinRegister   = byte(2)
	msgJoinData       = byte(3)
	msgHostData       = byte(4)
	msgToHost          = byte(5)
	msgToJoin          = byte(6)
	msgStatus          = byte(7)
	msgError           = byte(8)
	hostTimeout        = 30 * time.Second
	peerTimeout        = 30 * time.Second
	maxPacketBytes     = 65507
	defaultMaxRooms    = 256
	defaultMaxJoiners  = 24
)

type peer struct {
	addr *net.UDPAddr
	key  [16]byte
	seen time.Time
}

type room struct {
	hostAddr *net.UDPAddr
	hostKey  [16]byte
	hostSeen time.Time
	joiners  map[[16]byte]peer
}

type relayServer struct {
	conn        *net.UDPConn
	mu          sync.Mutex
	rooms       map[[16]byte]*room
	maxRooms    int
	maxJoiners  int
}

func main() {
	listen := flag.String("listen", "0.0.0.0:45000", "UDP listen address")
	maxRooms := flag.Int("max-rooms", defaultMaxRooms, "maximum simultaneous rooms")
	maxJoiners := flag.Int("max-joiners", defaultMaxJoiners, "maximum joiners per room")
	flag.Parse()

	addr, err := net.ResolveUDPAddr("udp4", *listen)
	if err != nil {
		log.Fatal(err)
	}
	conn, err := net.ListenUDP("udp4", addr)
	if err != nil {
		log.Fatal(err)
	}
	_ = conn.SetReadBuffer(4 << 20)
	_ = conn.SetWriteBuffer(4 << 20)

	s := &relayServer{
		conn:       conn,
		rooms:      make(map[[16]byte]*room),
		maxRooms:   *maxRooms,
		maxJoiners: *maxJoiners,
	}

	signals := make(chan os.Signal, 1)
	signal.Notify(signals, os.Interrupt, syscall.SIGTERM)
	go func() {
		<-signals
		log.Println("VZ relay shutting down")
		_ = conn.Close()
	}()

	go s.cleanupLoop()
	log.Printf("VZ UDP relay listening on %s (max rooms=%d, max joiners/room=%d)", addr, s.maxRooms, s.maxJoiners)

	buf := make([]byte, maxPacketBytes)
	for {
		n, remote, readErr := conn.ReadFromUDP(buf)
		if readErr != nil {
			if opErr, ok := readErr.(*net.OpError); ok && opErr.Err.Error() == "use of closed network connection" {
				return
			}
			log.Printf("UDP read error: %v", readErr)
			continue
		}
		if n < 5 || n > maxPacketBytes || string(buf[:4]) != magic {
			continue
		}
		packet := append([]byte(nil), buf[:n]...)
		s.handle(packet, cloneAddr(remote))
	}
}

func (s *relayServer) handle(packet []byte, remote *net.UDPAddr) {
	switch packet[4] {
	case msgHostRegister:
		// VZR1 | type | room-id[16] | host-secret[16]
		if len(packet) != 37 {
			return
		}
		var roomID, hostKey [16]byte
		copy(roomID[:], packet[5:21])
		copy(hostKey[:], packet[21:37])

		s.mu.Lock()
		r := s.rooms[roomID]
		if r == nil {
			if len(s.rooms) >= s.maxRooms {
				s.mu.Unlock()
				s.sendStatus(remote, 2, "relay is at its room limit")
				return
			}
			r = &room{hostKey: hostKey, joiners: make(map[[16]byte]peer)}
			s.rooms[roomID] = r
			log.Printf("room opened from %s (active rooms=%d)", remote, len(s.rooms))
		} else if !bytes.Equal(r.hostKey[:], hostKey[:]) {
			s.mu.Unlock()
			s.sendStatus(remote, 2, "room authentication failed")
			return
		}
		r.hostAddr = remote
		r.hostSeen = time.Now()
		s.mu.Unlock()
		s.sendStatus(remote, 1, "host registered")

	case msgJoinRegister:
		// VZR1 | type | room-id[16] | client-id[16] | client-key[16]
		if len(packet) != 53 {
			return
		}
		var roomID, clientID, clientKey [16]byte
		copy(roomID[:], packet[5:21])
		copy(clientID[:], packet[21:37])
		copy(clientKey[:], packet[37:53])

		s.mu.Lock()
		r := s.rooms[roomID]
		if r == nil || r.hostAddr == nil || time.Since(r.hostSeen) > hostTimeout {
			s.mu.Unlock()
			s.sendStatus(remote, 2, "host session is offline or expired")
			return
		}
		if _, exists := r.joiners[clientID]; !exists && len(r.joiners) >= s.maxJoiners {
			s.mu.Unlock()
			s.sendStatus(remote, 2, "room is full")
			return
		}
		r.joiners[clientID] = peer{addr: remote, key: clientKey, seen: time.Now()}
		s.mu.Unlock()
		s.sendStatus(remote, 1, "host is online")

	case msgJoinData:
		// VZR1 | type | room-id[16] | client-id[16] | client-key[16] | UDP payload
		if len(packet) < 54 {
			return
		}
		var roomID, clientID, clientKey [16]byte
		copy(roomID[:], packet[5:21])
		copy(clientID[:], packet[21:37])
		copy(clientKey[:], packet[37:53])

		s.mu.Lock()
		r := s.rooms[roomID]
		if r == nil || r.hostAddr == nil || time.Since(r.hostSeen) > hostTimeout {
			s.mu.Unlock()
			return
		}
		p, ok := r.joiners[clientID]
		if !ok || !sameAddr(p.addr, remote) || !bytes.Equal(p.key[:], clientKey[:]) {
			s.mu.Unlock()
			return
		}
		p.seen = time.Now()
		r.joiners[clientID] = p
		target := cloneAddr(r.hostAddr)
		s.mu.Unlock()

		out := make([]byte, 21+len(packet)-53)
		copy(out[:4], magic)
		out[4] = msgToHost
		copy(out[5:21], clientID[:])
		copy(out[21:], packet[53:])
		_, _ = s.conn.WriteToUDP(out, target)

	case msgHostData:
		// VZR1 | type | room-id[16] | host-secret[16] | client-id[16] | UDP payload
		if len(packet) < 54 {
			return
		}
		var roomID, hostKey, clientID [16]byte
		copy(roomID[:], packet[5:21])
		copy(hostKey[:], packet[21:37])
		copy(clientID[:], packet[37:53])

		s.mu.Lock()
		r := s.rooms[roomID]
		if r == nil || r.hostAddr == nil || !sameAddr(r.hostAddr, remote) || !bytes.Equal(r.hostKey[:], hostKey[:]) {
			s.mu.Unlock()
			return
		}
		r.hostSeen = time.Now()
		p, ok := r.joiners[clientID]
		if !ok || time.Since(p.seen) > peerTimeout {
			s.mu.Unlock()
			return
		}
		target := cloneAddr(p.addr)
		s.mu.Unlock()

		out := make([]byte, 5+len(packet)-53)
		copy(out[:4], magic)
		out[4] = msgToJoin
		copy(out[5:], packet[53:])
		_, _ = s.conn.WriteToUDP(out, target)
	}
}

func (s *relayServer) sendStatus(addr *net.UDPAddr, code byte, message string) {
	packet := make([]byte, 6+len(message))
	copy(packet[:4], magic)
	packet[4] = msgStatus
	packet[5] = code
	copy(packet[6:], message)
	_, _ = s.conn.WriteToUDP(packet, addr)
}

func (s *relayServer) cleanupLoop() {
	ticker := time.NewTicker(10 * time.Second)
	defer ticker.Stop()
	for range ticker.C {
		now := time.Now()
		s.mu.Lock()
		for id, r := range s.rooms {
			if r.hostAddr == nil || now.Sub(r.hostSeen) > hostTimeout {
				delete(s.rooms, id)
				continue
			}
			for clientID, p := range r.joiners {
				if now.Sub(p.seen) > peerTimeout {
					delete(r.joiners, clientID)
				}
			}
		}
		s.mu.Unlock()
	}
}

func sameAddr(a, b *net.UDPAddr) bool {
	return a != nil && b != nil && a.IP.Equal(b.IP) && a.Port == b.Port
}

func cloneAddr(a *net.UDPAddr) *net.UDPAddr {
	if a == nil {
		return nil
	}
	return &net.UDPAddr{IP: append(net.IP(nil), a.IP...), Port: a.Port, Zone: a.Zone}
}
