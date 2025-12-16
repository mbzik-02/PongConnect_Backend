using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;

namespace WebSockets
{
    //Die Klasse bearbeitet eingehende WebSocket-Anfragen und kümmert sich um die Kommunikation zwischen Clients
    public class HandleWebSocketClass
    {
        // Thread-sichere Liste aller verbundenen Clients (Key = zufällig generierte ID, Value = WebSocket-Objekt)
        ConcurrentDictionary<string, WebSocket> clients = new();

        // Wird für jede eingehende HTTP-Anfrage aufgerufen
        public async Task HandleWebSocket(HttpContext context, Func<Task> next)
        {
            // Prüft, ob die Anfrage auf den Pfad "/ws" abzielt — nur dann wird WebSocket-Kommunikation gestartet
            if (context.Request.Path == "/ws")
            {
                // Prüft, ob der Client tatsächlich eine WebSocket-Verbindung aufbauen möchte
                if (context.WebSockets.IsWebSocketRequest)
                {
                    //ein WebSocket-Objekt
                    WebSocket webSocket = await context.WebSockets.AcceptWebSocketAsync();

                    // Erzeugt eine eindeutige ID für diesen Client
                    string clientId = Guid.NewGuid().ToString();

                    // Fügt den Client in die Liste der verbundenen Clients ein
                    clients[clientId] = webSocket;

                    // Puffer für eingehende Nachrichten (4 KB)
                    byte[] buffer = new byte[1024 * 4];

                    // Solange die Verbindung offen ist, empfängt der Server Nachrichten
                    while (webSocket.State == WebSocketState.Open)
                    {
                        // Wartet auf eingehende Nachricht vom Client (blockierend)
                        WebSocketReceiveResult result = await webSocket.ReceiveAsync(
                            new ArraySegment<byte>(buffer),
                            CancellationToken.None
                        );

                        // Wenn der Client die Verbindung schließen möchte
                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            // Beendet sauber die Verbindung
                            await webSocket.CloseAsync(
                                WebSocketCloseStatus.NormalClosure,
                                "Closed by client",
                                CancellationToken.None
                            );

                            // Entfernt den Client aus der Liste
                            clients.TryRemove(clientId, out _);
                        }
                        else
                        {
                            // Dekodiert die empfangene Nachricht (UTF-8)
                            string msg = Encoding.UTF8.GetString(buffer, 0, result.Count);

                            // Sendet die Nachricht an alle verbundenen Clients (Broadcast)
                            foreach (WebSocket client in clients.Values)
                            {
                                if (client.State == WebSocketState.Open)
                                {
                                    // Kodiert die Nachricht als Bytefolge
                                    byte[] bytes = Encoding.UTF8.GetBytes(msg);

                                    // Sendet sie an den Client
                                    await client.SendAsync(
                                        bytes,
                                        WebSocketMessageType.Text,
                                        true, // zeigt an, dass die Nachricht komplett ist
                                        CancellationToken.None
                                    );
                                }
                            }
                        }
                    }
                }
                else
                {
                    // Wenn kein gültiger WebSocket-Request, sende Fehlercode 400 (Bad Request)
                    context.Response.StatusCode = 400;
                }
            }
            else
            {
                // Wenn Pfad nicht /ws ist, wird die nächste Middleware ausgeführt
                await next();
            }
        }
    }
}
