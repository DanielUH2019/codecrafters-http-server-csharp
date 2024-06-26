using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;


var server = new TcpListener(IPAddress.Any, 4221);
server.Start();
while (true)
{
    await HandleRequest(server, args);
}

static async Task HandleRequest(TcpListener server, string[] args)
{
    var socket = await server.AcceptSocketAsync();
    var networkStream = new NetworkStream(socket);
    var requestLines = (await ReadRequestAsLines(networkStream)).ToList();
    var requestLine = ParseRequestLine(requestLines[0]);
    var headersLines = requestLines.Skip(1).Take(requestLines.Count - 2).ToArray();
    var body = requestLines.Last();
    var responseBytes = BuildResponseBytes(requestLine, headersLines, body, args);
    await networkStream.WriteAsync(await responseBytes);
    socket.Close();
}

static async Task<byte[]> BuildResponseBytes(RequestLine requestLine, string[] headersLines, string body, string[] args) => requestLine.RequestTarget switch
{
    "/" => Encoding.UTF8.GetBytes(BuildResponseString("HTTP/1.1", 200)),
    var target when EchoRegex().IsMatch(target) => Encoding.UTF8.GetBytes(EchoHandler(target[6..])),
    "/user-agent" => Encoding.UTF8.GetBytes(UserAgentHandler(headersLines)),
    var target when FilesRegex().IsMatch(target) => Encoding.UTF8.GetBytes(await FilesHandlerAsync(requestLine, body, args)),
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

static async Task<string> FilesHandlerAsync(RequestLine requestLine, string content, string[] args)
{
    var fileName = requestLine.RequestTarget[7..];
    var filesPath = args[1];
    return requestLine.Method switch
    {
        HttpMethod.GET => GetFile(fileName, filesPath),
        HttpMethod.POST => await CreateFileAsync(fileName, filesPath, content),
        _ => BuildResponseString("HTTP/1.1", 405, "Method Not Allowed"),
    };
}

static string GetFile(string name, string path)
{
    var filePath = Path.Combine(path, name);
    if (!File.Exists(filePath))
    {
        return BuildResponseString("HTTP/1.1", 404, "Not Found");
    }

    var fileContent = File.ReadAllText(filePath);
    var headersBuilder = new StringBuilder();
    headersBuilder.Append("Content-Type: application/octet-stream\r\n");
    headersBuilder.Append($"Content-Length: {fileContent.Length}\r\n");
    var response = BuildResponseString("HTTP/1.1", 200, headers: headersBuilder.ToString(), body: fileContent);
    return response;
}

static async Task<string> CreateFileAsync(string name, string path, string content)
{
    var filePath = Path.Combine(path, name);
    if (File.Exists(filePath))
    {
        return BuildResponseString("HTTP/1.1", 409, "Conflict");
    }

    using (var sw = new StreamWriter(filePath))
    {
        await sw.WriteAsync(content);
    }

    return BuildResponseString("HTTP/1.1", 201, "Created");
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

    [GeneratedRegex(@"^/files/.*$")]
    private static partial Regex FilesRegex();
}