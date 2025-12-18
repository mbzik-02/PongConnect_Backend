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

                    //// Prüfen: sind schon 2 Clients da?
                    //if (clients.Count >= 2)
                    //{
                    //    // Optional: kurze Info an den Client senden
                    //    byte[] msg = Encoding.UTF8.GetBytes("room_full");
                    //    await webSocket.SendAsync(
                    //        msg,
                    //        WebSocketMessageType.Text,
                    //        true,
                    //        CancellationToken.None
                    //    );

                    //    await webSocket.CloseAsync(
                    //        WebSocketCloseStatus.PolicyViolation,
                    //        "Max 2 players allowed",
                    //        CancellationToken.None
                    //    );

                    //    //man darf nicht in die Schleife gehen
                    //    return; 
                    //}

                    // Erzeugt eine eindeutige ID für diesen Client
                    string clientId = Guid.NewGuid().ToString();

                    // Fügt den Client in die Liste der verbundenen Clients ein
                    clients[clientId] = webSocket;

                    // Puffer für eingehende Nachrichten (4 KB)
                    byte[] buffer = new byte[1024 * 4];

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
                            break; // Schleife wirklich verlassen
                        }

                        // Nachricht
                        string msg = Encoding.UTF8.GetString(buffer, 0, result.Count);
                        Console.WriteLine("Message: " + msg);

                        // Anzahl aktuell verbundener Clients bestimmen
                        int clientCount = clients.Count;

                        // Payload erweitern (z.B. als JSON, oder simples Protokoll)
                        string enrichedMsg = $"{msg}|clients={clientCount}";
                        Console.WriteLine("Message: " + enrichedMsg);

                        foreach (WebSocket client in clients.Values)
                        {
                            if (client.State == WebSocketState.Open)
                            {
                                byte[] bytes = Encoding.UTF8.GetBytes(enrichedMsg);

                                await client.SendAsync(
                                    bytes,
                                    WebSocketMessageType.Text,
                                    true,
                                    CancellationToken.None
                                );
                            }
                        }
                    }

                    // Falls noch nicht entfernt (z.B. bei Exception), cleanup:
                    clients.TryRemove(clientId, out _);

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
