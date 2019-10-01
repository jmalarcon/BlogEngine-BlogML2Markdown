using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
//using ReverseMarkdown; 

namespace BlogMLToMarkdown
{
    class Program
    {
        //CHANGE this to adapt it to your own needs
        private const string postFormat = @"---
layout: post.html
title: {0}
published: {1}
date: {2}
excerpt: {3}
categories: {4}
tags: {5}
authors: {6}
image: {7}
---

{8}
";
        private const string blogMLNamespace = "http://www.blogml.com/2006/09/BlogML";

        static void Main(string[] args)
        {
            string documentContent = null;
            using(var sr = new StreamReader(args[0]))
            {
                documentContent = sr.ReadToEnd();
            }

            var document = XDocument.Load(XmlReader.Create(new StringReader(documentContent), new XmlReaderSettings
                {
                    IgnoreComments = true,
                    CheckCharacters = false,
                }));

            var allCategories = document.Root.Elements(XName.Get("categories", blogMLNamespace))
                .Elements()
                .ToArray();

            var posts = document.Root.Elements(XName.Get("posts", blogMLNamespace))
                .Elements()
                .ToArray();

            int numPosts = posts.Length;
            int numProcessed = 0;

            foreach (var post in posts)
            {
                var dateCreated = DateTime.Parse(post.Attribute("date-created").Value).ToString("yyyy-MM-dd HH:mm:ss");
                var title = WrapInDoubleQuotes(post.Descendants(XName.Get("title", blogMLNamespace)).First().Value);

                numProcessed++;
                Console.WriteLine("Post {0} of {1}: {2}", numProcessed, numPosts, title);

                string folder = "posts";    //By default, save posts in this folder
                string postType = GetFileNameNoExt(post.Attribute("type").Value.ToLower());
                if (postType != "normal")
                    folder = "other";   //Folder to save everything that is not a post (articles, for example)

                string filename = GetFileNameNoExt(post.Attribute("post-url").Value);

                bool isPublished = post.Attribute("is-published").Value.ToLower() == "true";
                bool isApproved = post.Attribute("approved").Value.ToLower() == "true";
                if (!isPublished || !isApproved)
                    folder = "NotPublished";

                //Check if it has an excerpt
                bool hasExcerpt = post.Attribute("hasexcerpt").Value.ToLower() == "true";
                string excerpt = "";
                if (hasExcerpt)
                    try
                    {
                        //Not always has an excerpt although it says so
                        excerpt = WrapInDoubleQuotes(post.Descendants(XName.Get("excerpt", blogMLNamespace)).First().Value);
                    }
                    catch
                    {
                    }

                var content = post.Descendants(XName.Get("content", blogMLNamespace)).First().Value;
                var url = post.Attribute("post-url").Value;
                var postname = url.Substring(url.LastIndexOf("/") + 1).Replace(".aspx", "");
               
                var rawCategories = post.Descendants(XName.Get("category", blogMLNamespace))
                    .Select(c1 => c1.Attribute("ref").Value)
                    .ToArray();

                var categories = ConvertoToYamlArray(allCategories.Where(c1 => rawCategories.Any(c2 => c2 == c1.Attribute(XName.Get("id")).Value))
                    .Select(c1 => WrapInDoubleQuotes(c1.Elements().First().Value))
                    .ToArray());

                var tags = ConvertoToYamlArray(post.Descendants(XName.Get("tag", blogMLNamespace))
                    .Select(c1 => WrapInDoubleQuotes(c1.Attribute("ref").Value))
                    .ToArray());

                var authors = ConvertoToYamlArray(post.Descendants(XName.Get("author", blogMLNamespace))
                    .Select(c1 => WrapInDoubleQuotes(c1.Attribute("ref").Value))
                    .ToArray());

                var markdown = FormatCode(ConvertHtmlToMarkdown(content));
                //BlogEngine specific: remove the image.axd handler for images and substitute it for the "images folder
                //Change the folder name if you need to
                markdown = markdown.Replace("/image.axd?picture=/", "images/");
                markdown = markdown.Replace("/image.axd?picture=", "images/");  //Not all start with a slash

                //Extract first image and use it as the highlighted image of the post
                string image = "";
                Regex reMDImages = new Regex(@"\!\[.*?\]\(([^\)]*?)("".*?""){0,1}\)", RegexOptions.Singleline);
                Match firstImgMatch = reMDImages.Match(markdown);
                if (firstImgMatch.Success)
                {
                    image = firstImgMatch.Groups[1].Value;
                }

                var blog = string.Format(postFormat, 
                    title, //{0}
                    isPublished && isApproved ? "true" : "false" , //{1}
                    dateCreated, //{2}
                    excerpt, //{3}
                    categories, //{4}
                    tags, //{5}
                    authors,//{6}
                    image, //{7}
                    markdown);  //{8}

                if (!Directory.Exists("output\\" + folder))
                {
                    Directory.CreateDirectory("output\\" + folder);
                }
                using (var sw = File.CreateText("output\\" + folder + "\\" + filename + ".md"))
                {
                    sw.Write(blog);
                };

            }
            Console.WriteLine("All posts processed and generated!!");
            Console.ReadLine();
        }

        private static object ConvertoToYamlArray(string[] arr)
        {
            return string.Format("[{0}]", string.Join(", ", arr));
        }

        private static string WrapInDoubleQuotes(string value)
        {
            return @"""" + value + @"""";
        }

        private static string GetFileNameNoExt(string path)
        {
            int slashPos = path.LastIndexOf('/');  //Position of folder separator
            if (slashPos >= 0)
                path = path.Substring(slashPos + 1);
            int dotPos = path.LastIndexOf('.');
            //If there's no final point
            if (dotPos == -1)
                return path;    //The path is a file without extension
            //Remove everyting after the point position (included)
            return path.Substring(0, dotPos);
        }

        static readonly Regex _htmlCodeRegex = new Regex(@"<pre><code>(.*?)</code></pre>", RegexOptions.Singleline);
        static string ConvertHtmlToMarkdown(string source)
        {
            //Change the code blocks without a language specified in a general lang to create fenced code later
            source = _htmlCodeRegex.Replace(source, "<pre><code class=\"language-none\">$1</code></pre>");
            var converter = new Html2Markdown.Converter();
            string res = converter.Convert(source);
            res = res.Trim(' ', '\r', '\n');  //Remove extra spaces and new lines
            return res;

        }

        //static readonly Regex _codeRegex = new Regex(@"~~~~ \{\.csharpcode\}(?<code>.*?)~~~~", RegexOptions.Compiled | RegexOptions.Singleline);
        static readonly Regex _codeRegex = new Regex(@"<code class=""language-(.*?)""{0,1}>(.*?)</code>", RegexOptions.Singleline);

        static string FormatCode(string content)
        {
            return _codeRegex.Replace(content, "```$1\r\n$2```");
        }
    }
}
