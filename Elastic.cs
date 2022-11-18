using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

class Elastic
{
    public static async Task<List<string>> GetAlerts(string url, string cacertfile, bool allowInvalidHttpsCert, string elasticsearchUsername, string elasticsearchPassword, string alertingindex, string queryfile)
    {
        if (cacertfile != string.Empty && !File.Exists(cacertfile))
        {
            Logger.Error($"Couldn't find cacertfile: '{cacertfile}'");
            return new List<string>();
        }

        if (!File.Exists(queryfile))
        {
            Logger.Error($"Couldn't find queryfile: '{queryfile}'");
            return new List<string>();
        }

        var query = File.ReadAllText(queryfile);

        var result = await QueryIndex(url, cacertfile, allowInvalidHttpsCert, elasticsearchUsername, elasticsearchPassword, alertingindex, query);

        JToken hits;
        if (!TryParseJObject(result, out JObject jobject) || (hits = jobject["hits"]) == null || (hits = hits["hits"]) == null || !(hits is JArray jarray))
        {
            Logger.Error($"Couldn't parse result: '{result}'");
            return new List<string>();
        }

        var alerts = new List<string>();
        int alertCount = 0;
        foreach (var alert in jarray)
        {
            JToken token;
            if (!(alert is JObject jobjectAlert) || (token = jobjectAlert["_source"]) == null || !(token is JObject jobjectSource))
            {
                Logger.Error($"Couldn't parse alert {alertCount + 1}: '{alert}'");
                continue;
            }

            var rule_id = GetValueString(jobjectSource, "rule_id", alertCount);
            var rule_name = GetValueString(jobjectSource, "rule_name", alertCount);
            var alert_id = GetValueString(jobjectSource, "alert_id", alertCount);
            var context_message = GetValueString(jobjectSource, "context_message", alertCount);
            var timestamp = GetValueDate(jobjectSource, "@timestamp", alertCount);

            if (rule_id == string.Empty || rule_name == string.Empty || alert_id == string.Empty || timestamp == DateTime.MinValue)
            {
                Logger.Error($"Couldn't parse alert {alertCount + 1}: '{alert}'");
                continue;
            }

            alerts.Add($"{timestamp:yyyy-MM-dd HH:mm:ss}: {rule_name}: {context_message}");
            alertCount++;
        }

        return alerts;
    }

    static string GetValueString(JObject jobject, string propertyName, int ordinal)
    {
        var jtoken = jobject[propertyName];
        if (jtoken == null || !(jtoken is JValue jvalue))
        {
            Logger.Error($"Couldn't parse alert {ordinal + 1}: '{jobject}'");
            return string.Empty;
        }

        var value = jvalue.Value<string>();
        if (value == null || value == string.Empty)
        {
            Logger.Error($"Couldn't parse alert {ordinal + 1}: '{jobject}'");
            return string.Empty;
        }

        return value;
    }

    static DateTime GetValueDate(JObject jobject, string propertyName, int ordinal)
    {
        var jtoken = jobject[propertyName];
        if (jtoken == null || !(jtoken is JValue jvalue))
        {
            Logger.Error($"Couldn't parse alert {ordinal + 1}: '{jobject}'");
            return DateTime.MinValue;
        }

        var value = jvalue.Value<DateTime>();
        if (value == DateTime.MinValue)
        {
            Logger.Error($"Couldn't parse alert {ordinal + 1}: '{jobject}'");
            return DateTime.MinValue;
        }

        return value;
    }

    static bool TryParseJObject(string json, out JObject jobject)
    {
        try
        {
            jobject = JObject.Parse(json);
            return true;
        }
        catch (JsonReaderException)
        {
            jobject = new JObject();
            return false;
        }
    }

    static async Task<string> QueryIndex(string serverurl, string cacertfile, bool allowInvalidHttpsCert, string username, string password, string indexname, string query)
    {
        using var handler = new HttpClientHandler();

        if (allowInvalidHttpsCert)
        {
            handler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
        }
        else if (cacertfile != string.Empty)
        {
            var cacert = new X509Certificate2(cacertfile);

            handler.ServerCertificateCustomValidationCallback = (
                HttpRequestMessage message,
                X509Certificate2 cert,
                X509Chain chain,
                SslPolicyErrors errors
            ) => chain != null && chain.ChainElements.Count == 2 && chain.ChainElements[1].Certificate.RawData.SequenceEqual(cacert.RawData);
        }

        using var client = allowInvalidHttpsCert || cacertfile != string.Empty ? new HttpClient(handler) : new HttpClient();

        var address = $"{serverurl}/{indexname}/_search";

        string credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}"));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);

        string result = string.Empty;
        var request = new HttpRequestMessage
        {
            RequestUri = new Uri(address),
            Method = HttpMethod.Get,
            Content = new StringContent(query, Encoding.UTF8, "application/json")
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        try
        {
            var response = await client.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                Logger.Error($"Get '{address}', StatusCode: {response.StatusCode}, Query: >>>{query}<<<");
            }
            result = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                Logger.Error($"Result: >>>{result}<<<");
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"Get '{address}': >>>{query}<<<");
            Logger.Error($"Result: >>>{result}<<<");
            Logger.Error($"Exception: >>>{ex.ToString()}<<<");
        }

        return result;
    }
}
