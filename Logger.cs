using System;
using System.Collections.Generic;
using System.IO;

class Logger
{
    public static List<string> errors = new List<string>();

    public static void Information(string message)
    {
        Log(message, logLevel.information);
    }

    public static void Error(string message)
    {
        Log(message, logLevel.error);
    }

    enum logLevel
    {
        information,
        error
    }

    static void Log(string message, logLevel loglevel)
    {
        string withdate;
        if (loglevel == logLevel.error)
        {
            withdate = $"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}: ERROR: {message}";
        }
        else
        {
            withdate = $"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}: {message}";
        }
        Console.WriteLine(withdate);
        File.AppendAllText("alerter.log", $"{withdate}\n");
        if (loglevel == logLevel.error)
        {
            errors.Add(message);
        }
    }
}
