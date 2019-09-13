using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
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
        private static HttpClient webber = new HttpClient();
        private static Dictionary<string, string> fixedNames = new Dictionary<string, string>();

        
        
        static void Main(string[] args) {

            realMain().GetAwaiter().GetResult();



        }
        
        public static async Task realMain() {
            StreamReader file = new StreamReader("Token.txt");
            token = file.ReadLine();
            webber.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            
            file = new StreamReader("FixedNames.txt");
            string fixedNamesLine;
            while ((fixedNamesLine = file.ReadLine()) != null) {
                var names = fixedNamesLine.Split(',');
                fixedNames.Add(names[0].ToLower(), names[1].ToLower());
            }
            
            
            
            string courseCode = await getMostRecentCourseCode();
            Console.WriteLine($"Course code: {courseCode}");


            string path = "UFLCOP3503FoxFall2019_Introduction_to_zyLabs_and_C_report_2019-09-13_1227.csv";
            List<Student> students = convertZybooksCSV(path);
            

            List<string> sectionIDs = await getSectionIDs(courseCode);
            Console.WriteLine($"{sectionIDs.Count} sections found");


            var assignmentID = await getAssignmentIDbyName(courseCode, "Assignment[0]");
            Console.WriteLine($"Found assignment ID: {assignmentID}");
 
            Dictionary<string, string> userIDs = new Dictionary<string, string>();
            
            
//
//            foreach (var section in sectionIDs) {
//                await getUserIDsBySection(courseCode, section, userIDs);
//            }            

            
            string mySectionID = getSectionID(courseCode, "13038").Result;

            await getUserIDsBySection(courseCode, mySectionID, userIDs);

           



            int temp = await updateGrades(courseCode, students, userIDs, assignmentID);
            Console.WriteLine($"Finished with {temp} students");


        }


        /// <summary>
        /// Used to generate a text file named "BadNames.txt" containing mismatched student names between zybooks and canvas
        /// </summary>
        /// <param name="Zybooks List"></param>
        /// <param name="Canvas Dictionary"></param>
        public static async Task generateBadNames(List<Student> studentsFromFile,
            Dictionary<string, string> canvasStudents) {

            StreamWriter file = new StreamWriter("BadNames.txt");

            foreach (var student in studentsFromFile) {
                string studentName = (student.firstName + " " + student.lastName).ToLower();
                if (!canvasStudents.ContainsKey(studentName)) {
                    file.WriteLine(studentName);
                }
            }
            file.Close();
        }



        public static async Task<int> updateGrades(string courseID, List<Student> studentsFromFile,
            Dictionary<string, string> canvasStudents, string assignmentID) {


            int counter = 0;

            
            foreach (var student in studentsFromFile) {

                string studentName = (student.firstName + " " + student.lastName).ToLower();

                if (canvasStudents.ContainsKey(studentName)) {

                    var gradeURI = canvasAPIurl +
                                   $"courses/{courseID}/assignments/{assignmentID}/submissions/{canvasStudents[studentName]}";

                    Dictionary<string, string> temp = new Dictionary<string, string>();
                    temp.Add("submission[posted_grade", Convert.ToString(student.grade));
                    var payload = new FormUrlEncodedContent(temp);
                                   
                    var response = await webber.PutAsync(gradeURI, payload);

                    counter++;
     
                }


                else {

                    if (fixedNames.ContainsKey(studentName)) {

                        studentName = fixedNames[studentName];

                        if (!canvasStudents.ContainsKey(studentName)) {
                            continue;
                        }

                        var gradeURI = canvasAPIurl +
                                       $"courses/{courseID}/assignments/{assignmentID}/submissions/{canvasStudents[studentName]}";

                        Dictionary<string, string> temp = new Dictionary<string, string>();
                        temp.Add("submission[posted_grade", Convert.ToString(student.grade));
                        var payload = new FormUrlEncodedContent(temp);
                                   
                        var response = await webber.PutAsync(gradeURI, payload);

                        counter++;
                        

                    }
                    
                }
                
                if (counter % 10 == 0) {
                    Console.WriteLine($"{counter} students graded");
                }


            }


            return counter;
        }
        
        //Section needs to be given by canvas API ID (use the method below)
        public static async Task getUserIDsBySection(string courseID, string sectionID, Dictionary<string, string> results) {
            
            var sectionURI = canvasAPIurl + $"/courses/{courseID}/sections/{sectionID}?include[]=students";
            var response = webber.GetAsync(sectionURI).Result;
            var content = await response.Content.ReadAsStringAsync();
            JObject currSection = JsonConvert.DeserializeObject<JObject>(content);
                        
            foreach(var student in currSection["students"]) {                
                
                results.Add(((string)student["name"]).ToLower(), (string) student["id"]);

            }

        }
        
        //finds the first assignment with a name containing the string passed in
        public static async Task<string> getAssignmentIDbyName(string courseID, string assignmentName) {
            
            var assignmentsURI = canvasAPIurl + $"/courses/{courseID}/assignments";
            var response = webber.GetAsync(assignmentsURI).Result;
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
            
            var assignmentsURI = canvasAPIurl + $"/courses/{courseID}/assignments";
            var response = webber.GetAsync(assignmentsURI).Result;
            var content = await response.Content.ReadAsStringAsync();
            JArray obj = JsonConvert.DeserializeObject<JArray>(content);

            List<string> assignmentIDs = new List<string>();
            foreach(var temp in obj){
                assignmentIDs.Add((string)temp["id"]);
            }

            return assignmentIDs;
        }

        public static async Task<List<string>> getSectionIDs(string courseID) {


            var sectionURI = canvasAPIurl + $"/courses/{courseID}/sections?per_page=100";
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