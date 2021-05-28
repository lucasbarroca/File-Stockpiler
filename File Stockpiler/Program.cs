using HtmlAgilityPack;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Web;

namespace File_Stockpiler
{
    class Program
    {
        static AppConfig config;
        static Uri baseUri;
        
        static int filesFound = 0;
        static int filesToDownload = 0;
        static int filesDownloaded = 0;
        static int downloadsFailed = 0;

        static void Main(string[] args)
        {
            string jsonFileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), $"File Stockpiler{Path.DirectorySeparatorChar}config.json");
            config = new AppConfig();

            // Load json config file
            bool tryAgain = true;
            while (tryAgain)
            {
                tryAgain = false;
                if (File.Exists(jsonFileName))
                {
                    Console.WriteLine("Continue with saved options? (y/n):");
                    if (Console.ReadKey(true).Key == ConsoleKey.Y)
                    {
                        try
                        {
                            string jsonFile = File.ReadAllText(jsonFileName);
                            config = JsonConvert.DeserializeObject<AppConfig>(jsonFile);
                        }
                        catch
                        {
                            Console.WriteLine("Config file exists but can't be loaded. Try again? (y/n):");
                            if (Console.ReadKey(true).Key == ConsoleKey.Y)
                            {
                                tryAgain = true;
                            }

                        }
                    }
                }
            }

            // Check options
            bool optionsChanged = false;
            if (config.TargetUrl.Length < 1)
            {
                Console.WriteLine("Type the target url:");
                config.TargetUrl = Console.ReadLine();
                optionsChanged = true;
            }

            if (config.DownloadFileFormats.Length < 1)
            {
                Console.WriteLine("Type the file formats to download (separated by \",\" default: zip):");
                string formats = Console.ReadLine();
                if (formats.Trim().Length > 0)
                {
                    config.DownloadFileFormats = Console.ReadLine().Split(',');
                }
                else
                {
                    config.DownloadFileFormats = new string[] { "zip" };
                }

                optionsChanged = true;
            }

            if (config.LocalCheckFileFormats.Length < 1)
            {
                Console.WriteLine("Type the file formats to locally check if the file already exists (separated by \",\" default: zip,chd) :");
                string formats = Console.ReadLine();
                if (formats.Trim().Length > 0)
                {
                    config.LocalCheckFileFormats = Console.ReadLine().Split(',');
                }
                else
                {
                    config.LocalCheckFileFormats = new string[] { "zip", "chd" };
                }

                optionsChanged = true;
            }

            // Save options if changed?
            if (optionsChanged)
            {
                Console.WriteLine("Would you like to save options? (y/n):");
                if (Console.ReadKey(true).Key == ConsoleKey.Y)
                {
                    try
                    {
                        // Create directory if needed
                        string configDir = Path.GetDirectoryName(jsonFileName);
                        Directory.CreateDirectory(configDir);

                        // Write file
                        string jsonData = JsonConvert.SerializeObject(config);
                        File.WriteAllText(jsonFileName, jsonData);

                        Console.WriteLine("Options saved!");
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine("Error during saving options! details:");
                        Console.WriteLine(e.ToString());
                    }
                }
            }

            // Do the magic!
            if (Uri.TryCreate(config.TargetUrl, UriKind.Absolute, out baseUri))
            {
                Console.WriteLine("Loading website data...");

                List<string> filesList = new List<string>();

                bool preSetup = true;
                try
                {
                    HtmlWeb hw = new HtmlWeb();
                    HtmlDocument doc = hw.Load(config.TargetUrl);

                    Console.WriteLine("Building file list...");
                    HtmlNodeCollection hrefList = doc.DocumentNode.SelectNodes("//a[@href]");

                    foreach (HtmlNode link in hrefList)
                    {
                        string fileUrl = link.GetAttributeValue("href", "");

                        if (CheckFileExt(fileUrl))
                        {                         
                            filesFound++;

                            if (NeedDownload(fileUrl))
                            {
                                filesList.Add(fileUrl);
                                filesToDownload++;
                            }
                        }
                    }

                    Console.WriteLine($"Files found: {filesFound} / Need Download: {filesToDownload}");
                }
                catch (Exception e)
                {
                    preSetup = false;
                    Console.WriteLine("Pre-setup failed.");
                    Console.WriteLine(e.ToString());
                }

                // Start downloading
                if (preSetup)
                {
                    // Set fails log before start
                    SetupFailsFileName();

                    // Start file downloading
                    foreach (string file in filesList)
                    {
                        DownloadFile(file);
                    }

                    Console.WriteLine("Downloads Finished!");
                    Console.WriteLine($"Found: {filesFound} | Skipped: {filesFound - filesToDownload} | Downloaded: {filesDownloaded} | Fails: {downloadsFailed}");
                }
            }
            else
            {
                Console.WriteLine("Invalid url!");
            }

            // Prevent exit
            Console.WriteLine("Press any key to exit...");
            Console.ReadKey(true);
        }

        static bool DownloadFile(string fileUrl)
        {
            Uri fullUrl;
            bool fail = false;
            string failReason = "";

            // File have absolute url from html
            if (!Uri.TryCreate(fileUrl, UriKind.Absolute, out fullUrl))
            {
                // File have relative url from html
                if (!Uri.TryCreate(baseUri, fileUrl, out fullUrl))
                {
                    // File have invalid url
                    fail = true;
                    failReason = "invalid file url";
                }
            }

            // Try to download file
            if (!fail)
            {
                try
                {
                    string filename = Path.GetFileName(fullUrl.LocalPath);

                    Console.WriteLine($"{filesDownloaded + 1}/{filesToDownload} Downloading {filename}...");

                    using (WebClient wc = new WebClient())
                    {
                        //wc.DownloadFile(fullUrl.AbsoluteUri, filename);
                        filesDownloaded++;
                    }
                }
                catch (Exception e)
                {
                    fail = true;
                    failReason = $"download failed";
                    WriteFailsLog($"[{Path.GetFileName(fileUrl)}] details: \"{failReason}\" Exception: {e}");
                }
            }
            else
            {
                WriteFailsLog($"[{Path.GetFileName(fileUrl)}] details: {failReason}");
            }

            if (fail)
            {
                downloadsFailed++;
            }

            // Output error

            // Return download status
            return !fail;
        }

        static string failsFileName = "fails.txt";
        
        static void SetupFailsFileName()
        {
            bool fileSetup = false;
            int fileSuffix = 0;
            while (!fileSetup)
            {
                string filename = failsFileName;
                if (fileSuffix > 0)
                {
                    filename = $"{Path.GetFileNameWithoutExtension(failsFileName) + fileSuffix}.{Path.GetExtension(failsFileName)}";
                }

                if (File.Exists(filename))
                {
                    fileSuffix++;
                }
                else
                {
                    failsFileName = filename;
                    fileSetup = true;
                }
            }
        }

        static void WriteFailsLog(string line)
        {
            File.AppendAllText(failsFileName, line + "\r\n");
        }

        static bool CheckFileExt(string filename)
        {
            try
            {
                if (filename.Length < 1)
                {
                    return false;
                }

                return config.DownloadFileFormats.Contains(Path.GetExtension(filename).Replace(".", ""));
            }
            catch
            {
                return false;
            }

        }

        static bool NeedDownload(string fileUrl)
        {
            string filename = HttpUtility.UrlDecode(fileUrl);
            foreach (string ext in config.LocalCheckFileFormats)
            {
                if (RecursiveFileExists(Path.ChangeExtension(filename, ext)))
                {
                    return false;
                }
            }

            return true;
        }

        static string[] filesInDirectory;
        static bool RecursiveFileExists(string filename)
        {
            // Cache files list
            if (filesInDirectory == null)
            {
                Console.WriteLine("Caching directory list...");
                filesInDirectory = Directory.GetFiles(Directory.GetCurrentDirectory(), "*.*", SearchOption.AllDirectories);
            }

            var found = filesInDirectory.FirstOrDefault(s => Path.GetFileName(s) == filename);
            return found != null;
        }
    }

    class AppConfig
    {
        public string TargetUrl = "";
        public string[] DownloadFileFormats = { };
        public string[] LocalCheckFileFormats = { };
    }
}
