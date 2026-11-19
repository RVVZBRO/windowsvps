extends Node3D

const DEFAULT_PORT := 7777
const MAX_PLAYERS := 16
const WALK_SPEED := 7.0
const JUMP_VELOCITY := 5.5
const GRAVITY := 16.0

var players: Dictionary = {}
var local_id := 1
var server_mode := false
var last_sync := 0.0
var status_label: Label
var address_edit: LineEdit
var port_edit: LineEdit
var tunnel_edit: LineEdit
var public_edit: LineEdit
var name_edit: LineEdit
var map_dialog: FileDialog
var pwrp_dialog: FileDialog
var pwrp_path := ""
var loaded_map: Node3D
var camera: Camera3D

func _ready() -> void:
	_build_world()
	_build_ui()
	multiplayer.peer_connected.connect(_on_peer_connected)
	multiplayer.peer_disconnected.connect(_on_peer_disconnected)
	multiplayer.connected_to_server.connect(_on_connected)
	multiplayer.connection_failed.connect(_on_connection_failed)
	multiplayer.server_disconnected.connect(_on_server_disconnected)
	get_window().title = "VZ — Playable Client"

func _build_world() -> void:
	var env := WorldEnvironment.new()
	var environment := Environment.new()
	environment.background_mode = Environment.BG_SKY
	environment.sky = Sky.new()
	environment.ambient_light_source = Environment.AMBIENT_SOURCE_COLOR
	environment.ambient_light_color = Color(0.55, 0.62, 0.75)
	environment.ambient_light_energy = 0.8
	env.environment = environment
	add_child(env)
	var sun := DirectionalLight3D.new()
	sun.rotation_degrees = Vector3(-52, -28, 0)
	sun.shadow_enabled = true
	add_child(sun)
	var floor_body := StaticBody3D.new()
	var floor_shape := CollisionShape3D.new()
	var box := BoxShape3D.new()
	box.size = Vector3(100, 1, 100)
	floor_shape.shape = box
	floor_shape.position.y = -0.5
	floor_body.add_child(floor_shape)
	var floor_mesh := MeshInstance3D.new()
	var mesh := BoxMesh.new()
	mesh.size = box.size
	floor_mesh.mesh = mesh
	floor_mesh.position.y = -0.5
	var mat := StandardMaterial3D.new()
	mat.albedo_color = Color(0.18, 0.22, 0.27)
	floor_mesh.material_override = mat
	floor_body.add_child(floor_mesh)
	add_child(floor_body)
	for x in range(-4, 5):
		for z in range(-4, 5):
			if abs(x) + abs(z) < 3:
				continue
			var block := MeshInstance3D.new()
			var bm := BoxMesh.new()
			bm.size = Vector3(2, 1.5 + float((x * z) % 3 + 3) * 0.25, 2)
			block.mesh = bm
			block.position = Vector3(x * 5, bm.size.y / 2.0, z * 5)
			var block_mat := StandardMaterial3D.new()
			block_mat.albedo_color = Color(0.25 + float(abs(x) % 3) * 0.08, 0.32, 0.4)
			block.material_override = block_mat
			add_child(block)

func _build_ui() -> void:
	var layer := CanvasLayer.new()
	add_child(layer)
	var panel := PanelContainer.new()
	panel.position = Vector2(18, 18)
	panel.custom_minimum_size = Vector2(330, 0)
	layer.add_child(panel)
	var v := VBoxContainer.new()
	v.add_theme_constant_override("separation", 7)
	panel.add_child(v)
	var title := Label.new()
	title.text = "VZ  /  MULTIPLAYER"
	title.add_theme_font_size_override("font_size", 23)
	v.add_child(title)
	var subtitle := Label.new()
	subtitle.text = "VZ-native test build • WASD to move • Space to jump"
	subtitle.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	v.add_child(subtitle)
	name_edit = _line(v, "Player name", "Player")
	port_edit = _line(v, "Game UDP port", str(DEFAULT_PORT))
	address_edit = _line(v, "Host address / Portwarp address", "127.0.0.1")
	_add_button(v, "HOST LOCAL SESSION", _host_game)
	_add_button(v, "JOIN SESSION", _join_game)
	_add_button(v, "IMPORT GLB / GLTF MAP", _choose_map)
	tunnel_edit = _line(v, "Portwarp tunnel name (optional)", "")
	public_edit = _line(v, "Public address shown in Portwarp", "")
	_add_button(v, "CONNECT PORTWARP TUNNEL", _connect_portwarp)
	_add_button(v, "SELECT pwrp.exe", _choose_pwrp)
	_add_button(v, "OPEN PORTWARP DOWNLOADS", func(): OS.shell_open("https://portwarp.com/downloads"))
	status_label = Label.new()
	status_label.text = "Ready. Host here or join another VZ session."
	status_label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	v.add_child(status_label)
	var hint := Label.new()
	hint.text = "RBXL files are not executable in VZ. Import a converted .glb/.gltf scene."
	hint.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	hint.add_theme_color_override("font_color", Color(1.0, 0.78, 0.42))
	v.add_child(hint)
	map_dialog = FileDialog.new()
	map_dialog.file_mode = FileDialog.FILE_MODE_OPEN_FILE
	map_dialog.access = FileDialog.ACCESS_FILESYSTEM
	map_dialog.add_filter("*.glb, *.gltf", "glTF 2.0 map")
	map_dialog.file_selected.connect(_load_gltf)
	layer.add_child(map_dialog)
	pwrp_dialog = FileDialog.new()
	pwrp_dialog.file_mode = FileDialog.FILE_MODE_OPEN_FILE
	pwrp_dialog.access = FileDialog.ACCESS_FILESYSTEM
	pwrp_dialog.add_filter("*.exe", "Windows executable")
	pwrp_dialog.file_selected.connect(_select_pwrp)
	layer.add_child(pwrp_dialog)

func _line(parent: VBoxContainer, caption: String, value: String) -> LineEdit:
	var label := Label.new()
	label.text = caption
	parent.add_child(label)
	var edit := LineEdit.new()
	edit.text = value
	edit.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	parent.add_child(edit)
	return edit

func _add_button(parent: VBoxContainer, caption: String, action: Callable) -> void:
	var button := Button.new()
	button.text = caption
	button.custom_minimum_size.y = 34
	button.pressed.connect(action)
	parent.add_child(button)

func _set_status(message: String) -> void:
	if is_instance_valid(status_label):
		status_label.text = message

func _get_port() -> int:
	return clampi(int(port_edit.text), 1, 65535)

func _host_game() -> void:
	if multiplayer.multiplayer_peer != null:
		multiplayer.multiplayer_peer.close()
	var peer := ENetMultiplayerPeer.new()
	var error := peer.create_server(_get_port(), MAX_PLAYERS)
	if error != OK:
		_set_status("Could not host. ENet error: " + str(error))
		return
	multiplayer.multiplayer_peer = peer
	server_mode = true
	local_id = 1
	_spawn_player(1, name_edit.text)
	_set_status("Hosting VZ session on UDP " + str(_get_port()) + ". Connect Portwarp to expose it.")

func _join_game() -> void:
	if multiplayer.multiplayer_peer != null:
		multiplayer.multiplayer_peer.close()
	var peer := ENetMultiplayerPeer.new()
	var address := address_edit.text.strip_edges()
	if address.is_empty():
		_set_status("Enter the host address first.")
		return
	var error := peer.create_client(address, _get_port())
	if error != OK:
		_set_status("Could not start connection: " + str(error))
		return
	multiplayer.multiplayer_peer = peer
	server_mode = false
	_set_status("Connecting to " + address + ":" + str(_get_port()) + " ...")

func _on_connected() -> void:
	local_id = multiplayer.get_unique_id()
	_set_status("Connected! Peer ID " + str(local_id) + ". Click the world and use WASD.")
	rpc_id(1, "request_world_state")

func _on_connection_failed() -> void:
	_set_status("Connection failed. Check address, UDP port, server, and Portwarp tunnel.")
	multiplayer.multiplayer_peer = null

func _on_server_disconnected() -> void:
	_set_status("Host disconnected.")
	multiplayer.multiplayer_peer = null

func _on_peer_connected(id: int) -> void:
	if not server_mode:
		return
	rpc("spawn_player", id, "Player " + str(id))
	for existing_id in players.keys():
		if int(existing_id) != id:
			rpc_id(id, "spawn_player", int(existing_id), "Player " + str(existing_id))

func _on_peer_disconnected(id: int) -> void:
	if players.has(id):
		players[id].queue_free()
		players.erase(id)
	_set_status("Player " + str(id) + " left.")

@rpc("any_peer", "reliable")
func request_world_state() -> void:
	if not server_mode:
		return
	var requester := multiplayer.get_remote_sender_id()
	for id in players.keys():
		rpc_id(requester, "spawn_player", int(id), "Player " + str(id))

@rpc("authority", "call_local", "reliable")
func spawn_player(id: int, player_name: String) -> void:
	if players.has(id):
		return
	var body := CharacterBody3D.new()
	body.name = "Player_" + str(id)
	body.position = Vector3(float((id * 3) % 9) - 4, 1.2, float((id * 5) % 9) - 4)
	var shape_node := CollisionShape3D.new()
	var capsule := CapsuleShape3D.new()
	capsule.radius = 0.42
	capsule.height = 1.8
	shape_node.shape = capsule
	body.add_child(shape_node)
	var visual := MeshInstance3D.new()
	var capsule_mesh := CapsuleMesh.new()
	capsule_mesh.radius = 0.42
	capsule_mesh.height = 1.8
	visual.mesh = capsule_mesh
	var visual_mat := StandardMaterial3D.new()
	visual_mat.albedo_color = Color(0.2, 0.65, 1.0) if id == 1 else Color(1.0, 0.55, 0.25)
	visual.material_override = visual_mat
	body.add_child(visual)
	if id == local_id:
		var pivot := Node3D.new()
		pivot.position.y = 0.55
		body.add_child(pivot)
		camera = Camera3D.new()
		camera.position = Vector3(0, 0.5, 3.8)
		camera.rotation_degrees.x = -8
		pivot.add_child(camera)
		camera.current = true
	add_child(body)
	players[id] = body

@rpc("any_peer", "unreliable")
func submit_player_state(pos: Vector3, yaw: float) -> void:
	if not server_mode:
		return
	var sender := multiplayer.get_remote_sender_id()
	if sender <= 0 or not players.has(sender):
		return
	rpc("apply_player_state", sender, pos, yaw)

@rpc("authority", "call_local", "unreliable")
func apply_player_state(id: int, pos: Vector3, yaw: float) -> void:
	if id == local_id or not players.has(id):
		return
	players[id].global_position = pos
	players[id].rotation.y = yaw

func _physics_process(delta: float) -> void:
	if not players.has(local_id):
		return
	var body: CharacterBody3D = players[local_id]
	var direction := Vector3.ZERO
	direction.x = Input.get_action_strength("move_right") - Input.get_action_strength("move_left")
	direction.z = Input.get_action_strength("move_back") - Input.get_action_strength("move_forward")
	direction = direction.normalized()
	if direction.length() > 0.01:
		body.rotation.y = atan2(-direction.x, -direction.z)
	var move := Vector3(direction.x, 0, direction.z).normalized()
	body.velocity.x = move.x * WALK_SPEED
	body.velocity.z = move.z * WALK_SPEED
	if not body.is_on_floor():
		body.velocity.y -= GRAVITY * delta
	elif Input.is_action_just_pressed("jump"):
		body.velocity.y = JUMP_VELOCITY
	body.move_and_slide()
	last_sync += delta
	if last_sync >= 0.05 and multiplayer.multiplayer_peer != null:
		last_sync = 0.0
		if server_mode:
			rpc("apply_player_state", local_id, body.global_position, body.rotation.y)
		else:
			rpc_id(1, "submit_player_state", body.global_position, body.rotation.y)

func _unhandled_input(event: InputEvent) -> void:
	if event is InputEventMouseButton and event.pressed and players.has(local_id):
		Input.mouse_mode = Input.MOUSE_MODE_CAPTURED
	elif event is InputEventKey and event.pressed and event.keycode == KEY_ESCAPE:
		Input.mouse_mode = Input.MOUSE_MODE_VISIBLE

func _choose_map() -> void:
	map_dialog.popup_centered(Vector2i(800, 600))

func _load_gltf(path: String) -> void:
	var document := GLTFDocument.new()
	var state := GLTFState.new()
	var error := document.append_from_file(path, state)
	if error != OK:
		_set_status("Could not import glTF map. Error: " + str(error))
		return
	var scene := document.generate_scene(state)
	if scene == null:
		_set_status("The glTF file did not produce a scene.")
		return
	if loaded_map != null:
		loaded_map.queue_free()
	loaded_map = scene
	add_child(loaded_map)
	_set_status("Imported map: " + path.get_file())

func _choose_pwrp() -> void:
	pwrp_dialog.popup_centered(Vector2i(800, 600))

func _select_pwrp(path: String) -> void:
	pwrp_path = path
	_set_status("Portwarp CLI selected: " + path)

func _connect_portwarp() -> void:
	var tunnel := tunnel_edit.text.strip_edges()
	if pwrp_path.is_empty() or not FileAccess.file_exists(pwrp_path):
		_set_status("Select pwrp.exe first. Download from portwarp.com/downloads.")
		return
	if tunnel.is_empty():
		_set_status("Enter the exact tunnel name from your Portwarp dashboard.")
		return
	var output: Array = []
	var exit_code := OS.execute(pwrp_path, PackedStringArray(["connect", tunnel, "--detach"]), output, true)
	var details := "\n".join(PackedStringArray(output))
	if exit_code == 0:
		_set_status("Portwarp connected. Public endpoint: " + public_edit.text + " " + details)
	else:
		_set_status("Portwarp failed (" + str(exit_code) + "): " + details)

func _exit_tree() -> void:
	if multiplayer.multiplayer_peer != null:
		multiplayer.multiplayer_peer.close()
