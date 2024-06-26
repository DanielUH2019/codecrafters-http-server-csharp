
using System.IO.Compression;
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
    "/" => BuildResponse("HTTP/1.1", 200),
    var target when EchoRegex().IsMatch(target) => EchoHandler(target[6..], headersLines),
    "/user-agent" => UserAgentHandler(headersLines),
    var target when FilesRegex().IsMatch(target) => await FilesHandlerAsync(requestLine, body, args),
    _ => BuildResponse("HTTP/1.1", 404, "Not Found")
};

static byte[] BuildResponse(string httpVersion, int statusCode, string phrase = "OK", string headers = "", string body = "", bool useCompression = false)
{
    var response = new StringBuilder($"{httpVersion} {statusCode} {phrase}\r\n");
    if (useCompression)
    {
        var compressedText = GzipCompressText(body);
        var headersBuilder = new StringBuilder(headers);
        headersBuilder.Append("Content-Encoding: gzip\r\n");
        headersBuilder.Append($"Content-Length: {compressedText.Length}\r\n");
        response.Append($"{headersBuilder.ToString()}\r\n");
        var responseBytes = Encoding.UTF8.GetBytes(response.ToString());
        var finalResponse = new byte[responseBytes.Length + compressedText.Length];
        Buffer.BlockCopy(responseBytes, 0, finalResponse, 0, responseBytes.Length);
        Buffer.BlockCopy(compressedText, 0, finalResponse, responseBytes.Length, compressedText.Length);
        return finalResponse;
    }
    response.Append($"{headers}\r\n");
    response.Append(body);
    return Encoding.UTF8.GetBytes(response.ToString());
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

static string? ParseAcceptEncoding(string[] headersLines)
{
    var acceptEncoding = headersLines.FirstOrDefault(x => x.StartsWith("Accept-Encoding:"));
    var encodings = acceptEncoding?.Split(": ")[1].Split(", ");
    var supportedEncoding = encodings?.FirstOrDefault(x => x.Equals("gzip"));
    return supportedEncoding;
}

static byte[] EchoHandler(string text, string[] headersLines)
{
    var headersBuilder = new StringBuilder();

    var encoding = ParseAcceptEncoding(headersLines);
    // byte[] compressedText;
    var useCompression = encoding == "gzip";
    if (!useCompression)
    {
        headersBuilder.Append($"Content-Length: {text.Length}\r\n");
    }
    headersBuilder.Append("Content-Type: text/plain\r\n");
    var response = BuildResponse("HTTP/1.1", 200, headers: headersBuilder.ToString(), body: text, useCompression: useCompression);
    return response;
}

static byte[] UserAgentHandler(string[] headersLines)
{
    var userAgent = headersLines[1];
    var value = userAgent.Split(": ")[1];
    // Build response
    var headersBuilder = new StringBuilder();
    headersBuilder.Append("Content-Type: text/plain\r\n");
    headersBuilder.Append($"Content-Length: {value.Length}\r\n");
    var response = BuildResponse("HTTP/1.1", 200, headers: headersBuilder.ToString(), body: value);
    return response;
}

static async Task<byte[]> FilesHandlerAsync(RequestLine requestLine, string content, string[] args)
{
    var fileName = requestLine.RequestTarget[7..];
    var filesPath = args[1];
    return requestLine.Method switch
    {
        HttpMethod.GET => GetFile(fileName, filesPath),
        HttpMethod.POST => await CreateFileAsync(fileName, filesPath, content),
        _ => BuildResponse("HTTP/1.1", 405, "Method Not Allowed"),
    };
}

static byte[] GetFile(string name, string path)
{
    var filePath = Path.Combine(path, name);
    if (!File.Exists(filePath))
    {
        return BuildResponse("HTTP/1.1", 404, "Not Found");
    }

    var fileContent = File.ReadAllText(filePath);
    var headersBuilder = new StringBuilder();
    headersBuilder.Append("Content-Type: application/octet-stream\r\n");
    headersBuilder.Append($"Content-Length: {fileContent.Length}\r\n");
    var response = BuildResponse("HTTP/1.1", 200, headers: headersBuilder.ToString(), body: fileContent);
    return response;
}

static async Task<byte[]> CreateFileAsync(string name, string path, string content)
{
    var filePath = Path.Combine(path, name);
    if (File.Exists(filePath))
    {
        return BuildResponse("HTTP/1.1", 409, "Conflict");
    }

    using (var sw = new StreamWriter(filePath))
    {
        await sw.WriteAsync(content);
    }

    return BuildResponse("HTTP/1.1", 201, "Created");
}

static byte[] GzipCompressText(string text)
{
    using var outputStream = new MemoryStream();
    using (var gzipStream = new GZipStream(outputStream, CompressionMode.Compress))
    using (var writer = new StreamWriter(gzipStream))
    {
        writer.Write(text);
    }
    return outputStream.ToArray();
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