using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;


var server = new TcpListener(IPAddress.Any, 4221);
server.Start();

while (true)
{
    await HandleRequest(server);
}

static async Task HandleRequest(TcpListener server)
{
    var socket = await server.AcceptSocketAsync();
    var networkStream = new NetworkStream(socket);
    var requestLines = (await ReadRequestAsLines(networkStream)).ToList();
    var requestLine = ParseRequestLine(requestLines[0]);
    var responseBytes = BuildResponseBytes(requestLine, requestLines.Skip(1).ToArray());
    await networkStream.WriteAsync(responseBytes);
    socket.Close();
}

static byte[] BuildResponseBytes(RequestLine requestLine, string[] headersLines) => requestLine.RequestTarget switch
{
    "/" => Encoding.UTF8.GetBytes(BuildResponseString("HTTP/1.1", 200)),
    var target when EchoRegex().IsMatch(target) => Encoding.UTF8.GetBytes(EchoHandler(target[6..])),
    "/user-agent" => Encoding.UTF8.GetBytes(UserAgentHandler(headersLines)),
    _ => Encoding.UTF8.GetBytes(BuildResponseString("HTTP/1.1", 404, "Not Found"))
};

static string BuildResponseString(string httpVersion, int statusCode, string phrase = "OK", string headers = "", string body = "")
{
    var response = new StringBuilder($"{httpVersion} {statusCode} {phrase}\r\n");
    response.Append($"{headers}\r\n");
    response.Append(body);
    return response.ToString();
}


static RequestLine ParseRequestLine(string requestLine)
{
    var lineParts = requestLine.Split(" ");
    var httpMethod = Enum.TryParse<HttpMethod>(lineParts[0], out var method) ? method : throw new Exception("Invalid HTTP method");
    var requestTarget = lineParts[1];
    var httpVersion = lineParts[2];

    return new RequestLine(httpMethod, requestTarget, httpVersion);
}

static async Task<IEnumerable<string>> ReadRequestAsLines(NetworkStream stream)
{
    var buffer = new byte[1024];
    var receivedData = new StringBuilder();

    int bytesReceived;
    while ((bytesReceived = await stream.ReadAsync(buffer)) > 0)
    {
        receivedData.Append(Encoding.UTF8.GetString(buffer, 0, bytesReceived));
        if (receivedData.ToString().Contains("\r\n\r\n"))
        {
            break; // End of request
        }
    }

    var finalData = receivedData.ToString();
    return finalData.Split("\r\n");
}

static string EchoHandler(string text)
{
    var headersBuilder = new StringBuilder();
    headersBuilder.Append("Content-Type: text/plain\r\n");
    headersBuilder.Append($"Content-Length: {text.Length}\r\n");
    var response = BuildResponseString("HTTP/1.1", 200, headers: headersBuilder.ToString(), body: text);
    return response;
}

static string UserAgentHandler(string[] headersLines)
{
    var userAgent = headersLines[1];
    var value = userAgent.Split(": ")[1];
    // Build response
    var headersBuilder = new StringBuilder();
    headersBuilder.Append("Content-Type: text/plain\r\n");
    headersBuilder.Append($"Content-Length: {value.Length}\r\n");
    var response = BuildResponseString("HTTP/1.1", 200, headers: headersBuilder.ToString(), body: value);
    return response;
}

record RequestLine(HttpMethod Method, string RequestTarget, string HttpVersion);

enum HttpMethod
{
    GET,
    POST,
    PUT,
    DELETE
}



partial class Program
{
    [GeneratedRegex(@"^/echo/.*$")]
    private static partial Regex EchoRegex();
}