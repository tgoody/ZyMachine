using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
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
        private static string canvasAPIurl = "https://ufl.beta.instructure.com/api/v1/"; 
        private static HttpClient webber = new HttpClient();

        
        
        static void Main(string[] args) {
            
            StreamReader file = new StreamReader("Token.txt");
            token = file.ReadLine();
            webber.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            
            
            string courseCode = getMostRecentCourseCode().Result;


            string path = "UFLCOP3503FoxFall2019_Lab_2_report_2019-09-11_2059.csv";
            List<Student> students = convertZybooksCSV(path);
            

            //List<string> temp = getSectionIDs(courseCode).Result;

            string mySectionID = getSectionID(courseCode, "12735").Result;

            var assignmentID = getAssignmentIDbyName(courseCode, "Lab 2").Result;


            var userIDs = getUserIDsBySection(courseCode, mySectionID).Result;

        }



        //Section needs to be given by canvas API ID (use the method below)
        public static async Task<List<Tuple<string, string>>> getUserIDsBySection(string courseID, string sectionID) {
            
            var sectionURI = canvasAPIurl + $"/courses/{courseID}/sections/{sectionID}?include[]=students";
            var response = webber.GetAsync(sectionURI).Result;
            var content = await response.Content.ReadAsStringAsync();
            JObject currSection = JsonConvert.DeserializeObject<JObject>(content);
            //var studentArray = currSection["students"];
            
            List<Tuple<string, string>> results = new List<Tuple<string, string>>();
            
            foreach(var student in currSection["students"]) {

                var temp = new Tuple<string, string>((string) student["name"], (string) student["id"]);
                results.Add(temp);

            }

            return results;

        }
        
        //finds the first assignment with a name containing the string passed in
        public static async Task<string> getAssignmentIDbyName(string courseID, string assignmentName) {
            
            var sectionURI = canvasAPIurl + $"/courses/{courseID}/assignments";
            var response = webber.GetAsync(sectionURI).Result;
            var content = await response.Content.ReadAsStringAsync();
            JArray obj = JsonConvert.DeserializeObject<JArray>(content);
            
            foreach(var temp in obj){
                if (((string) temp["name"]).Contains(assignmentName)) {
                    return (string)temp["id"];
                }
            }

            return "No matching assignment found! Error";
        }
        
        public static async Task<List<string>> getAssignmentIDs(string courseID) {
            
            var sectionURI = canvasAPIurl + $"/courses/{courseID}/assignments";
            var response = webber.GetAsync(sectionURI).Result;
            var content = await response.Content.ReadAsStringAsync();
            JArray obj = JsonConvert.DeserializeObject<JArray>(content);

            List<string> assignmentIDs = new List<string>();
            foreach(var temp in obj){
                assignmentIDs.Add((string)temp["id"]);
            }

            return assignmentIDs;
        }

        public static async Task<List<string>> getSectionIDs(string courseID) {


            var sectionURI = canvasAPIurl + $"/courses/{courseID}/sections";
            var response = webber.GetAsync(sectionURI).Result;
            var content = await response.Content.ReadAsStringAsync();
            JArray obj = JsonConvert.DeserializeObject<JArray>(content);
            List<string> sectionIDs = new List<string>();
            foreach (var temp in obj) {
                sectionIDs.Add((string)temp["id"]);
            }


            return sectionIDs;
        }

        
        //must use 5 digit identifier (4 digit combo may be found in different 5 digit identifier)
        public static async Task<string> getSectionID(string courseID, string sectionNumber) {
            
            var sectionURI = canvasAPIurl + $"/courses/{courseID}/sections?per_page=100";
            var response = webber.GetAsync(sectionURI).Result;
            var content = await response.Content.ReadAsStringAsync();
            JArray obj = JsonConvert.DeserializeObject<JArray>(content);
            
            foreach (var temp in obj) {

                if (((string) temp["name"]).Contains(sectionNumber)) {
                    return (string)temp["id"];
                }
                
            }


            return "error, no section found w/ section number";
        }
            
           
        static async Task<string> getMostRecentCourseCode() {
            
            
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
            
            
            
            


            return (string)mostRecentCourse["id"];

        }

        public static List<Student> convertZybooksCSV(string path) {
            
            StreamReader file = new StreamReader(path);

            string line;

            line = file.ReadLine();
            
            var dataPoints = line.Split(',');

            int lastNameIndex = 0, firstNameIndex = 1, emailIndex = 2, percentageIndex = 5, gradeIndex = 6;

            //lord forgive me I have sinned
            for (int i = 0; i < dataPoints.Length; i++) {

                var headerName = dataPoints[i].ToLower();
                
                if (headerName.Contains("last name")) {
                    lastNameIndex = i;
                }
                else if (headerName.Contains("first name")) {
                    firstNameIndex = i;
                }
                else if (headerName.Contains("email")) {
                    emailIndex = i;
                }
                else if (headerName.Contains("percent")) {
                    percentageIndex = i;
                }
                else if (headerName.Contains("earned")) {
                    gradeIndex = i;
                }
                
            }

            List<Student> students = new List<Student>();
  
            while ((line=file.ReadLine()) != null) {

                dataPoints = line.Split(',');
                Student newStudent;

                newStudent.lastName = dataPoints[lastNameIndex];
                newStudent.firstName = dataPoints[firstNameIndex];
                newStudent.email = dataPoints[emailIndex];
                newStudent.percentage = Convert.ToDecimal(dataPoints[percentageIndex]);
                newStudent.grade = Convert.ToDecimal(dataPoints[gradeIndex]);

                students.Add(newStudent);
            }


            return students;

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