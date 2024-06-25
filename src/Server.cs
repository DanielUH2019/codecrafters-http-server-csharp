using System.Net;
using System.Net.Sockets;
using System.Text;


var server = new TcpListener(IPAddress.Any, 4221);
server.Start();
var socket = server.AcceptSocket(); // wait for client
socket.Send(Encoding.UTF8.GetBytes(GetSuccessfulResponse("HTTP/1.1", 200)));



static string GetSuccessfulResponse(string httpVersion, int statusCode, string phrase = "OK", string headers = "", string body = "")
{
    var response = new StringBuilder($"{httpVersion} {statusCode} {phrase}\r\n");
    response.Append($"{headers}\r\n");
    response.Append(body);
    return response.ToString();
}