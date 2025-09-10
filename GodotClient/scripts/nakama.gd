extends Node

var client : NakamaClient
var socket : NakamaSocket

func _ready():
    var scheme = "http"
    var host = "127.0.0.1"
    var port = 7350
    var server_key = "defaultServerAccessKey"
    client = Nakama.create_client(server_key, host, port, scheme)

    var username = "LucyAzalea"
    var password = "gQ70[9{!w*sa"
    var session : NakamaSession = await client.authenticate_email_async(username + "@projectcomet.com", password, username)
    if session.is_exception():
        print("An error occurred: %s" % session)
        return
    print("Successfully authenticated: %s" % session)

    socket = Nakama.create_socket_from(client)
    var connected : NakamaAsyncResult = await socket.connect_async(session)
    if connected.is_exception():
        print("An error occurred: %s" % connected)
        return
    print("Socket connected.")
