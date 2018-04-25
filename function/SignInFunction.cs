using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Table;
using Microsoft.Azure.SignalR;

namespace OnlineSignIn
{
    public class SignInInfo : TableEntity
    {
        public string OS { get; set; }
        public string Browser { get; set; }

        public SignInInfo(string os, string browser)
        {
            PartitionKey = "SignIn";
            RowKey = Guid.NewGuid().ToString();
            OS = os;
            Browser = browser;
        }

        public SignInInfo()
        {
        }
    }

    class SignInStats
    {
        public int totalNumber;
        public Dictionary<string, int> byOS = new Dictionary<string, int>();
        public Dictionary<string, int> byBrowser = new Dictionary<string, int>();
    }

    public static class SignInFunction
    {
        [FunctionName("signin")]
        public static async Task<HttpResponseMessage> Run([HttpTrigger(AuthorizationLevel.Anonymous, "get", "post")]HttpRequestMessage req, TraceWriter log)
        {
            var os = req.GetQueryNameValuePairs().FirstOrDefault(q => q.Key == "os").Value;
            var browser = req.GetQueryNameValuePairs().FirstOrDefault(q => q.Key == "browser").Value;
            dynamic data = await req.Content.ReadAsAsync<object>();
            os = os ?? data?.os;
            browser = browser ?? data?.browser;

            if (os == null) return req.CreateErrorResponse(HttpStatusCode.BadRequest, "Missing os");
            if (browser == null) return req.CreateErrorResponse(HttpStatusCode.BadRequest, "Missing browser");

            var account = CloudStorageAccount.Parse(Environment.GetEnvironmentVariable("TableConnectionString"));
            var client = account.CreateCloudTableClient();
            var table = client.GetTableReference("SignInInfo");
            // Insert sign-in info
            var newInfo = new SignInInfo(os, browser);
            var insert = TableOperation.Insert(newInfo);
            table.Execute(insert);

            var query = new TableQuery<SignInInfo>();
            var result = new SignInStats();
            foreach (var info in table.ExecuteQuery(query))
            {
                result.totalNumber++;
                if (!result.byBrowser.ContainsKey(info.Browser)) result.byBrowser[info.Browser] = 0;
                if (!result.byOS.ContainsKey(info.OS)) result.byOS[info.OS] = 0;
                result.byBrowser[info.Browser]++;
                result.byOS[info.OS]++;
            }

            var connectionString = Environment.GetEnvironmentVariable("AzureSignalRConnectionString");
            var proxy = CloudSignalR.CreateHubProxyFromConnectionString(connectionString, "chat");
            await proxy.Clients.All.SendAsync("broadcastMessage", new object[] { "_BROADCAST_", $"Current time is: {DateTime.Now}" });

            return req.CreateResponse(HttpStatusCode.OK, result, "application/json");
        }
    }
}
