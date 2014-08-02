﻿using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Net;
using System.Text;
using System.Web;
using System.Web.Hosting;
using System.Xml.Linq;
using System.Xml.XPath;

public static class Storage
{
    private static string _folder = HostingEnvironment.MapPath("~/posts/");

    public static List<Post> GetAllPosts()
    {
        if (HttpRuntime.Cache["posts"] == null)
            LoadPosts();

        if (HttpRuntime.Cache["posts"] != null)
        {
            return (List<Post>)HttpRuntime.Cache["posts"];
        }
        return new List<Post>();
    }

    // Can this be done async?
    public static void Save(Post post)
    {
        string file = Path.Combine(_folder, post.ID + ".xml");
        post.LastModified = DateTime.UtcNow;

        XDocument doc = new XDocument(
                        new XElement("post",
                            new XElement("title", post.Title),
                            new XElement("slug", post.Slug),
                            new XElement("author", post.Author),
                            new XElement("pubDate", post.PubDate.ToString("yyyy-MM-dd HH:mm:ss")),
                            new XElement("lastModified", post.LastModified.ToString("yyyy-MM-dd HH:mm:ss")),
                            new XElement("excerpt", post.Excerpt),
                            new XElement("content", post.Content),
                            new XElement("ispublished", post.IsPublished),
                            new XElement("categories", string.Empty),
                            new XElement("comments", string.Empty)
                        ));

        XElement categories = doc.XPathSelectElement("post/categories");
        foreach (string category in post.Categories)
        {
            categories.Add(new XElement("category", category));
        }

        XElement comments = doc.XPathSelectElement("post/comments");
        foreach (Comment comment in post.Comments)
        {
            comments.Add(
                new XElement("comment",
                    new XElement("author", comment.Author),
                    new XElement("email", comment.Email),
                    new XElement("website", comment.Website),
                    new XElement("ip", comment.Ip),
                    new XElement("userAgent", comment.UserAgent),
                    new XElement("date", comment.PubDate.ToString("yyyy-MM-dd HH:m:ss")),
                    new XElement("content", comment.Content),
                    new XAttribute("isAdmin", comment.IsAdmin),
                    new XAttribute("isApproved", comment.IsApproved),
                    new XAttribute("id", comment.ID)
                ));
        }

        if (!File.Exists(file)) // New post
        {
            var posts = GetAllPosts();
            posts.Insert(0, post);
            posts.Sort((p1, p2) => p2.PubDate.CompareTo(p1.PubDate));
            HttpRuntime.Cache.Insert("posts", posts);
        }
        else
        {
            Blog.ClearStartPageCache();
        }

        doc.Save(file);

        bool doPushToGit;
        if (bool.TryParse(ConfigurationManager.AppSettings["storage:git:enabled"], out doPushToGit) && doPushToGit)
        {
            PushFileToGitHub(post, doc);
        }
    }

    static void PushFileToGitHub(Post post, XDocument doc)
    {
        HttpWebRequest request = CreateGithubRequest(post);
        request.Method = "PUT";

        using (Stream stream = new MemoryStream())
        {
            using (var writer = new StreamWriter(stream))
            {
                writer.WriteLine("{");
                writer.WriteLine(" \"commiter\": {");
                writer.WriteLine("      \"name\": \"{0}\",", ConfigurationManager.AppSettings["storage:git:username"]);
                writer.WriteLine("      \"email\": \"{0}\"", ConfigurationManager.AppSettings["storage:git:email"]);
                writer.WriteLine("},");
                writer.WriteLine("  \"message\": \"{0}\",", post.Title);
                writer.Write("  \"content\": \"");
                writer.Write(ToBase64String(doc.ToString()));
                writer.WriteLine("\"");
                writer.Write("}");
                writer.Flush();

                request.ContentLength = stream.Length;

                using (var requestStream = request.GetRequestStream())
                {
                    stream.Position = 0;
                    stream.CopyTo(requestStream, 512);
                }
                request.GetResponse();
            }
        }
    }

    static string ToBase64String(string str)
    {
        return Convert.ToBase64String(Encoding.ASCII.GetBytes(str));
    }

    public static void Delete(Post post)
    {
        var posts = GetAllPosts();
        string file = Path.Combine(_folder, post.ID + ".xml");
        File.Delete(file);
        posts.Remove(post);
        Blog.ClearStartPageCache();

        HttpWebRequest request = CreateGithubRequest(post);
        request.Method = "Delete";
        request.GetResponse();
    }

    static HttpWebRequest CreateGithubRequest(Post post)
    {
        string repo = ConfigurationManager.AppSettings["storage:git:repo"];
        string email = ConfigurationManager.AppSettings["storage:git:email"];
        string password = ConfigurationManager.AppSettings["storage:git:password"];

        var request = (HttpWebRequest)WebRequest.Create(string.Format("https://api.github.com/repos/{0}/contents/Website/posts/{1}", repo, post.ID + ".xml"));
        request.UserAgent = "Miniblog";

        string token = ToBase64String(string.Format("{0}:{1}", email, password));
        request.Headers[HttpRequestHeader.Authorization] = string.Format("Basic {0}", token);
        return request;
    }

    private static void LoadPosts()
    {
        if (!Directory.Exists(_folder))
            Directory.CreateDirectory(_folder);

        List<Post> list = new List<Post>();

        // Can this be done in parallel to speed it up?
        foreach (string file in Directory.EnumerateFiles(_folder, "*.xml", SearchOption.TopDirectoryOnly))
        {
            XElement doc = XElement.Load(file);

            Post post = new Post()
            {
                ID = Path.GetFileNameWithoutExtension(file),
                Title = ReadValue(doc, "title"),
                Author = ReadValue(doc, "author"),
                Excerpt = ReadValue(doc, "excerpt"),
                Content = ReadValue(doc, "content"),
                Slug = ReadValue(doc, "slug").ToLowerInvariant(),
                PubDate = DateTime.Parse(ReadValue(doc, "pubDate")),
                LastModified = DateTime.Parse(ReadValue(doc, "lastModified", DateTime.Now.ToString())),
                IsPublished = bool.Parse(ReadValue(doc, "ispublished", "true")),
            };

            LoadCategories(post, doc);
            LoadComments(post, doc);
            list.Add(post);
        }

        if (list.Count > 0)
        {
            list.Sort((p1, p2) => p2.PubDate.CompareTo(p1.PubDate));
            HttpRuntime.Cache.Insert("posts", list);
        }
    }

    private static void LoadCategories(Post post, XElement doc)
    {
        XElement categories = doc.Element("categories");
        if (categories == null)
            return;

        List<string> list = new List<string>();

        foreach (var node in categories.Elements("category"))
        {
            list.Add(node.Value);
        }

        post.Categories = list.ToArray();
    }
    private static void LoadComments(Post post, XElement doc)
    {
        var comments = doc.Element("comments");

        if (comments == null)
            return;

        foreach (var node in comments.Elements("comment"))
        {
            Comment comment = new Comment()
            {
                ID = ReadAttribute(node, "id"),
                Author = ReadValue(node, "author"),
                Email = ReadValue(node, "email"),
                Website = ReadValue(node, "website"),
                Ip = ReadValue(node, "ip"),
                UserAgent = ReadValue(node, "userAgent"),
                IsAdmin = bool.Parse(ReadAttribute(node, "isAdmin", "false")),
                IsApproved = bool.Parse(ReadAttribute(node, "isApproved", "true")),
                Content = ReadValue(node, "content").Replace("\n", "<br />"),
                PubDate = DateTime.Parse(ReadValue(node, "date", "2000-01-01")),
            };

            post.Comments.Add(comment);
        }
    }

    private static string ReadValue(XElement doc, XName name, string defaultValue = "")
    {
        if (doc.Element(name) != null)
            return doc.Element(name).Value;

        return defaultValue;
    }

    private static string ReadAttribute(XElement element, XName name, string defaultValue = "")
    {
        if (element.Attribute(name) != null)
            return element.Attribute(name).Value;

        return defaultValue;
    }
}