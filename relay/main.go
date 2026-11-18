package main

import (
	"log"
	"net"
	"strings"
	"sync"
	"time"
)

type peer struct { addr *net.UDPAddr; seen time.Time }
var mu sync.Mutex
var rooms = map[string]map[string]peer{}

func main() {
	pc, err := net.ListenPacket("udp", ":45000")
	if err != nil { log.Fatal(err) }
	log.Println("VZ NepTunnel UDP relay listening on :45000")
	buf := make([]byte, 65507)
	for {
		n, addr, err := pc.ReadFrom(buf); if err != nil { continue }
		packet := append([]byte(nil), buf[:n]...)
		parts := strings.SplitN(string(packet), "|", 4)
		if len(parts) >= 3 && parts[0] == "VZ1" && (parts[2] == "host" || parts[2] == "join") {
			room, role := parts[1], parts[2]
			if len(room) < 8 || len(room) > 64 { continue }
			mu.Lock()
			if rooms[room] == nil { rooms[room] = map[string]peer{} }
			rooms[room][role] = peer{addr: addr, seen: time.Now()}
			other := "host"; if role == "host" { other = "join" }
			target := rooms[room][other].addr
			mu.Unlock()
			if target != nil { _, _ = pc.WriteTo([]byte("VZ1|"+room+"|READY"), addr) }
			continue
		}
		if len(parts) >= 3 && parts[0] == "DATA" {
			room, role := parts[1], parts[2]
			if role != "host" && role != "join" { continue }
			other := "host"; if role == "host" { other = "join" }
			mu.Lock(); p := rooms[room][other]; mu.Unlock()
			if p.addr != nil { _, _ = pc.WriteTo(packet, p.addr) }
		}
	}
}
