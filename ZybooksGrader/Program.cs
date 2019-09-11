using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ZybooksGrader {
    class Program {

        private static string token;
        private static string canvasAPIurl = "https://ufl.instructure.com/api/v1/"; 
        
        
        static void Main(string[] args) {
            Console.WriteLine("Hello World!");
            
            StreamReader file = new StreamReader("Token.txt");
            token = file.ReadLine();

            string courseCode = getMostRecentCourseCode().Result;




        }
        
        static async Task<string> getMostRecentCourseCode() {
            
            HttpClient webber = new HttpClient();
            webber.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            
            var response = webber.GetAsync(canvasAPIurl + "courses?enrollment_type=ta").Result;

            var content = await response.Content.ReadAsStringAsync();
            
            JArray obj = JsonConvert.DeserializeObject<JArray>(content);

            JToken mostRecentCourse = obj[0];
            DateTime mostRecentTime = (DateTime)obj[0]["start_at"];
            
            foreach (JToken res in obj)
            {
                if (DateTime.Compare((DateTime) res["start_at"], mostRecentTime) > 0) {
                    mostRecentCourse = res;
                    mostRecentTime = (DateTime)res["start_at"];
                }
                
            }
            
            
            
            
            Console.WriteLine(mostRecentCourse);


            return content;

        }
        
        
        
        
        
        
        
        
        
        
        //credit:https://brockallen.com/2016/09/24/process-start-for-urls-on-net-core/
        public static void OpenBrowser(string url)
        {
            try
            {
                Process.Start(url);
            }
            catch
            {
                // hack because of this: https://github.com/dotnet/corefx/issues/10361
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    url = url.Replace("&", "^&");
                    Process.Start(new ProcessStartInfo("cmd", $"/c start {url}") { CreateNoWindow = true });
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    Process.Start("xdg-open", url);
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    Process.Start("open", url);
                }
                else
                {
                    throw;
                }
            }
        }
        
        
    }
}