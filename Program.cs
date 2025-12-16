using System.Net.WebSockets;
using WebSockets;

// Erstellt einen neuen WebApplicationBuilder – Basis für eine minimal API bzw. ein Webserver-Projekt
WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// Baut die Anwendung (aus dem Builder wird jetzt eine lauffähige App)
WebApplication app = builder.Build();

// Konfiguriert Optionen für WebSockets
WebSocketOptions webSocketOptions = new WebSocketOptions()
{
    // Intervall, in dem ein Keep-Alive-Ping gesendet wird, um Verbindungen offen zu halten (hier: alle 30 Sekunden)
    KeepAliveInterval = TimeSpan.FromSeconds(30),
};

// Middleware aktivieren, damit die App WebSocket-Anfragen unterstützen kann
app.UseWebSockets(webSocketOptions);

// die Kommunikation über WebSockets verarbeitet
HandleWebSocketClass handleWebSocket = new HandleWebSocketClass();
app.Use(handleWebSocket.HandleWebSocket);

// Startet den Webserver und wartet auf Anfragen
app.Run();
