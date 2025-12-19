using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace WebSockets

{

    //Die Klasse bearbeitet eingehende WebSocket-Anfragen und kümmert sich um die Kommunikation zwischen Clients
    public class HandleWebSocketClass
    {
        // Thread-sichere Liste aller verbundenen Clients (Key = zufällig generierte ID, Value = WebSocket-Objekt)
        ConcurrentDictionary<string, WebSocket> clients = new();
        ConcurrentDictionary<string, int> playerMap = new();

        // Wird für jede eingehende HTTP-Anfrage aufgerufen
        public async Task HandleWebSocket(HttpContext context, Func<Task> next)
        {
            // Prüft, ob die Anfrage auf den Pfad "/ws" abzielt — nur dann wird WebSocket-Kommunikation gestartet
            if (context.Request.Path != "/ws")
            {
                await next();
                return;
            }

            if (!context.WebSockets.IsWebSocketRequest)
            {
                context.Response.StatusCode = 400;
                return;
            }

            WebSocket webSocket = await context.WebSockets.AcceptWebSocketAsync();
            await SendJson(webSocket, new
            {
                type = "SERVER_IP",
                ip = GetServerIp(),
                port = 4200
            });


            // Erzeugt eine eindeutige ID für diesen Client
            string clientId = Guid.NewGuid().ToString();

            // Fügt den Client in die Liste der verbundenen Clients ein   
            clients[clientId] = webSocket;
            string role = "";
            var buffer = new byte[1024 * 4];
            var initReceive = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
            if (initReceive.Count == 0)
            {
                role = "controller";
            }
            else
            {
                var initMsg = Encoding.UTF8.GetString(buffer, 0, initReceive.Count);
                var initdoc = JsonDocument.Parse(initMsg);
                role = initdoc.RootElement.GetProperty("role").GetString()!;
            }

            int playerId = 0;
            if (role == "controller")
            {
                playerId = AssignPlayerId();
                if (playerId == 0)
                {
                    // Raum voll
                    await SendJson(webSocket, new { type = "ROOM_FULL" });
                    await webSocket.CloseAsync(WebSocketCloseStatus.PolicyViolation, "Max 2 players allowed", CancellationToken.None);
                    clients.TryRemove(clientId, out _);
                    return;

                }

                playerMap[clientId] = playerId;
                await SendJson(webSocket, new
                {
                    type = "ASSIGN_PLAYER",
                    player = playerId
                });
            }

            await Broadcast(new
            {
                type = "PLAYERS",
                count = playerMap.Count
            });

            // Solange die Verbindung offen ist, empfängt der Server Nachrichten
            while (webSocket.State == WebSocketState.Open)
            {
                WebSocketReceiveResult result;
                try
                {
                    result = await webSocket.ReceiveAsync(
                        new ArraySegment<byte>(buffer),
                        CancellationToken.None
                    );
                }
                catch (WebSocketException) // z.B. ConnectionClosedPrematurely
                {
                    // Client ist „unsauber“ weg -> Verbindung aufräumen
                    break;
                }

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await webSocket.CloseAsync(
                        result.CloseStatus ?? WebSocketCloseStatus.NormalClosure,
                        result.CloseStatusDescription ?? "Closed by client",
                        CancellationToken.None
                    );

                    clients.TryRemove(clientId, out _);
                    playerMap.TryRemove(clientId, out _);
                    await Broadcast(new { type = "PLAYERS", count = playerMap.Count });
                    break;
                }

                // Nachricht
                string msg = Encoding.UTF8.GetString(buffer, 0, result.Count);

                Console.WriteLine("Message: " + msg);

                try
                {
                    var doc = JsonDocument.Parse(msg);

                    if (doc.RootElement.GetProperty("type").GetString() == "INPUT")
                    {
                        var input = JsonSerializer.Deserialize<Dictionary<string, object>>(msg)!;
                        input["player"] = playerMap[clientId];
                        await Broadcast(input);
                        continue;
                    }
                }
                catch
                {

                }
            }

            clients.TryRemove(clientId, out _);
            playerMap.TryRemove(clientId, out _);
            await Broadcast(new
            {
                type = "PLAYERS",
                count = playerMap.Count
            });

            if (webSocket.State == WebSocketState.Open)
            {
                await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closed", CancellationToken.None);
            }

        }

        private int AssignPlayerId()
        {
            if (!playerMap.Values.Contains(1))
                return 1;
            if (!playerMap.Values.Contains(2))
                return 2;

            return 0;
        }


        private async Task Broadcast(object payload)
        {
            string json = JsonSerializer.Serialize(payload);
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            foreach (var client in clients.Values)
            {
                if (client.State == WebSocketState.Open)
                    await client.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
            }
        }

        private async Task SendJson(WebSocket socket, object payload)
        {
            string json = JsonSerializer.Serialize(payload);
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            await socket.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
        }

        private string GetServerIp()
        {

            return System.Net.Dns.GetHostEntry(System.Net.Dns.GetHostName())
                .AddressList
                .First(ip => ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                .ToString();
        }

    }

}

