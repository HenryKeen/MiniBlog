using System;
using System.Configuration;
using System.IO;
using System.Net;
using System.ServiceModel.Activities;
using System.Text;
using System.Web.Helpers;
using System.Xml.Linq;

static internal class Git
{
    static string RepoName
    {
        get { return ConfigurationManager.AppSettings["storage:git:repo"]; }
    }

    static string UserName
    {
        get { return ConfigurationManager.AppSettings["storage:git:username"]; }
    }

    static string Email
    {
        get { return ConfigurationManager.AppSettings["storage:git:email"]; }
    }

    static string Password
    {
        get { return ConfigurationManager.AppSettings["storage:git:password"]; }
    }

    public static void SaveFile(Post post, XDocument doc)
    {
        string fileName = post.ID + ".xml";
        string sha = GetFileSHA(fileName);

        var title = (sha == null) ? "Create" : "Update";

        var body = new
        {
            commiter = new
            {
                name = UserName,
                email = Email
            },
            message = string.Format("{0} post: {1}", title, post.Title),
            sha = sha,
            content = ToBase64String(doc.ToString())
        };
        SendRequest(fileName, body, "PUT");
    }

    public static void DeleteFile(Post post)
    {
        string fileName = post.ID + ".xml";
        string sha = GetFileSHA(fileName);

        var body = new
        {
            commiter = new
            {
                name = UserName,
                email = Email
            },
            message = "Delete post: " + post.Title,
            sha = sha
        };
        SendRequest(fileName, body, "DELETE");
    }

    private static void SendRequest(string fileName, object body, string method)
    {
        HttpWebRequest request = CreateRequest(fileName);
        request.Method = method;

        using (Stream stream = new MemoryStream())
        {
            using (var writer = new StreamWriter(stream))
            {
                writer.Write(Json.Encode(body));
                writer.Flush();

                request.ContentLength = stream.Length;

                using (var requestStream = request.GetRequestStream())
                {
                    stream.Position = 0;
                    stream.CopyTo(requestStream, 1024);
                }
                request.GetResponse();
            }
        }
    }

    private static string GetFileSHA(string fileName)
    {
        try
        {
            HttpWebRequest request = CreateRequest(fileName);
            request.Method = "GET";
            
            var response = (HttpWebResponse)request.GetResponse();
            if (response.StatusCode == HttpStatusCode.OK)
            {
                using (var reader = new StreamReader(response.GetResponseStream()))
                {
                    dynamic data = Json.Decode(reader.ReadToEnd());
                    return data.sha;
                }
            }
        }
        catch (WebException) {}

        return null;
    }

    private static HttpWebRequest CreateRequest(string fileName)
    {
        var request = (HttpWebRequest)WebRequest.Create(String.Format("https://api.github.com/repos/{0}/contents/Website/posts/{1}", RepoName, fileName));
        request.UserAgent = "Miniblog";

        string token = ToBase64String(String.Format("{0}:{1}", Email, Password));
        request.Headers[HttpRequestHeader.Authorization] = String.Format("Basic {0}", token);
        return request;
    }

    private static string ToBase64String(string str)
    {
        return Convert.ToBase64String(Encoding.ASCII.GetBytes(str));
    }
}