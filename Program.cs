using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Linq;
using System.Net.Mail;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

class Program
{
    static async Task<int> Main(string[] args)
    {
        try
        {
            return await Run(args);
        }
        catch (Exception ex)
        {
            Logger.Error(ex.ToString());
            return 1;
        }
    }

    static async Task<int> Run(string[] args)
    {
        if (args.Length != 1 || !File.Exists(args[0]))
        {
            Logger.Error(@"Usage: alerter <configfile>");
            return 1;
        }

        var configFile = args[0];
        var configContents = File.ReadAllText(configFile);
        var config = JObject.Parse(configContents);

        var url = config["elasticsearch"]["url"].Value<string>();
        var cacertfile = config["elasticsearch"]["cacertfile"].Value<string>();
        var alertingindex = config["elasticsearch"]["alertingindex"].Value<string>();
        var queryfile = config["elasticsearch"]["queryfile"].Value<string>();
        var elasticsearchUsername = config["elasticsearch"]["username"].Value<string>();
        var elasticsearchPassword = config["elasticsearch"]["password"].Value<string>();

        var to = config["smtp"]["to"].Value<string>();
        var from = config["smtp"]["from"].Value<string>();
        var subject = config["smtp"]["subject"].Value<string>();
        var smtpServer = config["smtp"]["smtpserver"].Value<string>();
        var smtpUsername = config["smtp"]["username"].Value<string>();
        var smtpPassword = config["smtp"]["password"].Value<string>();

        var alerts = new List<string>();
        try
        {
            alerts = await Elastic.GetAlerts(url, cacertfile, cacertfile != null, elasticsearchUsername, elasticsearchPassword, alertingindex, queryfile);
        }
        catch (Exception ex)
        {
            Logger.Error(ex.ToString());
        }

        FilterAlerts(alerts);

        if (alerts.Count > 0)
        {
            var message = FormatAlerts(alerts);
            SendEmail(to, from, subject, message, smtpServer, smtpUsername, smtpPassword);
        }

        SilenceAlerts(alerts);

        Logger.Information("Done!");

        return 0;
    }

    static string FormatAlerts(List<string> alerts)
    {
        if (Logger.errors.Count > 0)
        {
            alerts.Add($"{DateTime.UtcNow:yyyy-MM-dd}: Problems when retrieving alerts, check log files.");
        }

        var message = $"{alerts.Count} alerts triggered:\n\n{string.Join("\n\n", alerts)}";

        return message;
    }

    static void SendEmail(string to, string from, string subject, string body, string smtpServer, string username, string password)
    {
        Logger.Information($"Using: to: '{to}', from: '{from}', subject: '{subject}', body: '{body.Substring(0, 3)}...', smtpserver: '{smtpServer}', string '{username}', password: '{password.Substring(0, 3)}...'");
        Logger.Information($">>>{body}<<<");

        SmtpClient smtpClient;
        int separator = smtpServer.IndexOf(':');
        if (separator < 0)
        {
            smtpClient = new SmtpClient(smtpServer);
        }
        else
        {
            var s = smtpServer.Substring(separator + 1);
            if (int.TryParse(s, out int port))
            {
                smtpClient = new SmtpClient(smtpServer.Substring(0, separator), port);
            }
            else
            {
                Logger.Error($"Invalid port: '{s}'");
                return;
            }
        }

        smtpClient.Credentials = new NetworkCredential(username, password);

        var message = new MailMessage(from, to, subject, body);
        smtpClient.EnableSsl = true;

        smtpClient.Send(message);

        // Sleep a little while, required for sending email to some smtp servers.
        Thread.Sleep(5000);
    }

    static void FilterAlerts(List<string> alerts)
    {
        var filename = "alerter_cache.txt";
        var oldalerts = File.Exists(filename) ? File.ReadAllLines(filename).Select(a => a.Replace("\\n", "\n")) : Array.Empty<string>();

        for (int i = 0; i < alerts.Count;)
        {
            if (oldalerts.Contains(alerts[i]))
            {
                Logger.Information($"Ignoring already triggered alert: {alerts[i].Replace("\n", "\\n")}");
                alerts.RemoveAt(i);
            }
            else
            {
                i++;
            }
        }
    }

    static void SilenceAlerts(List<string> alerts)
    {
        var filename = "alerter_cache.txt";
        var oldalerts = File.Exists(filename) ? File.ReadAllLines(filename) : Array.Empty<string>();

        var allalerts = oldalerts.Concat(alerts).Distinct().ToArray();
        File.WriteAllLines(filename, allalerts.Select(a => a.Replace("\n", "\\n")));

        var newcount = allalerts.Length - oldalerts.Length;
        if (newcount > 0)
        {
            Logger.Information($"Added {newcount} triggered alerts to trigger cache.");
        }
    }
}
