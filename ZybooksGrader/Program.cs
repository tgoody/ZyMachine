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
        private static string canvasAPIurl = "https://ufl.beta.instructure.com/api/v1/"; 
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


            string path = "UFLCOP3503FoxFall2019_Project_1_-_Linked_List_report_2019-10-07_0017.csv";
            List<Student> students = convertZybooksCSV(path);
            

            List<string> sectionIDs = await getSectionIDs(courseCode);
            Console.WriteLine($"{sectionIDs.Count} sections found");


            var assignmentID = await getAssignmentIDbyName(courseCode, "Project 1");
            Console.WriteLine($"Found assignment ID: {assignmentID}");
 
            Dictionary<string, string> userIDs = new Dictionary<string, string>();
            
            
            //Choose either this:
            foreach (var section in sectionIDs) {
                await getUserIDsBySection(courseCode, section, userIDs);
            }            


            //Or this:
//            string mySectionID = getSectionID(courseCode, "13034").Result;
//            await getUserIDsBySection(courseCode, mySectionID, userIDs);

  
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



        /// <summary>
        /// The actual grading method, matching students from Canvas to students from Zybooks
        /// </summary>
        /// <param name="Canvas Course ID"></param>
        /// <param name="List of Students from Zybooks CSV"></param>
        /// <param name="Dictionary of Students from Canvas"></param>
        /// <param name="Assignment ID from Canvas"></param>
        /// <returns>Number of students graded</returns>
        public static async Task<int> updateGrades(string courseID, List<Student> studentsFromFile,
            Dictionary<string, string> canvasStudents, string assignmentID) {


            int counter = 0;

            
            foreach (var student in studentsFromFile) {

                string studentName = (student.firstName + " " + student.lastName).ToLower();

                if (canvasStudents.ContainsKey(studentName)) {

                    var gradeURI = canvasAPIurl +
                                   $"courses/{courseID}/assignments/{assignmentID}/submissions/{canvasStudents[studentName]}";

                    var response = await webber.GetAsync(gradeURI);
                    var content = await response.Content.ReadAsStringAsync();
                    JObject jsonData = JsonConvert.DeserializeObject<JObject>(content);

                    var stringCheck = jsonData["grade"].ToString();
                    
                    if (stringCheck != "") {
                        
                        Console.WriteLine("Grade not null");
                        continue;

                    }

                    Dictionary<string, string> temp = new Dictionary<string, string>();
                    temp.Add("submission[posted_grade", Convert.ToString(student.grade));
                    var payload = new FormUrlEncodedContent(temp);
                                   
                    response = await webber.PutAsync(gradeURI, payload);

                    counter++;
     
                    Console.WriteLine($"{counter} students graded");

                }


                else {

                    if (fixedNames.ContainsKey(studentName)) {

                        studentName = fixedNames[studentName];

                        if (!canvasStudents.ContainsKey(studentName)) {
                            continue;
                        }

                        var gradeURI = canvasAPIurl +
                                       $"courses/{courseID}/assignments/{assignmentID}/submissions/{canvasStudents[studentName]}";

                        var response = await webber.GetAsync(gradeURI);

                        var content = await response.Content.ReadAsStringAsync();
                        JObject jsonData = JsonConvert.DeserializeObject<JObject>(content);
                        var stringCheck = jsonData["grade"].ToString();
                    
                        if (stringCheck != "") {
                        
                            Console.WriteLine("Grade not null");
                            continue;

                        }
                        
                        
                        Dictionary<string, string> temp = new Dictionary<string, string>();
                        temp.Add("submission[posted_grade", Convert.ToString(student.grade));
                        var payload = new FormUrlEncodedContent(temp);
                                   
                        response = await webber.PutAsync(gradeURI, payload);

                        counter++;
                        
                        Console.WriteLine($"{counter} students graded");
                        

                    }
                    
                }
                
               


            }


            return counter;
        }
        
        
        /// <summary>
        /// Store a list of students from a particular section (by Canvas supplied Section ID) and store them in the dictionary
        /// </summary>
        /// <param name="courseID"></param>
        /// <param name="sectionID"></param>
        /// <param name="Canvas Dictionary"></param>
        /// <returns></returns>
        public static async Task getUserIDsBySection(string courseID, string sectionID, Dictionary<string, string> results) {
            
            var sectionURI = canvasAPIurl + $"/courses/{courseID}/sections/{sectionID}?include[]=students";
            var response = webber.GetAsync(sectionURI).Result;
            var content = await response.Content.ReadAsStringAsync();
            JObject currSection = JsonConvert.DeserializeObject<JObject>(content);
                        
            foreach(var student in currSection["students"]) {                
                
                results.Add(((string)student["name"]).ToLower(), (string) student["id"]);

            }

        }
        
        /// <summary>
        /// Finds the first assignment with a name containing the string passed in
        /// </summary>
        /// <param name="courseID"></param>
        /// <param name="assignmentName"></param>
        /// <returns></returns>
        public static async Task<string> getAssignmentIDbyName(string courseID, string assignmentName) {
            
            var assignmentsURI = canvasAPIurl + $"/courses/{courseID}/assignments?per_page=100";
            var response = webber.GetAsync(assignmentsURI).Result;
            var content = await response.Content.ReadAsStringAsync();
            JArray obj = JsonConvert.DeserializeObject<JArray>(content);
            
            foreach(var temp in obj){
                
                Console.WriteLine((string)temp["name"]);
                
                if (((string) temp["name"]).Contains(assignmentName)) {
                    return (string)temp["id"];
                }
            }

            return "No matching assignment found! Error";
        }
        
        
        /// <summary>
        /// Get a list of all assignment IDs from Canvas
        /// </summary>
        /// <param name="courseID"></param>
        /// <returns></returns>
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

        
        /// <summary>
        /// Get a list of all section IDs from canvas
        /// </summary>
        /// <param name="courseID"></param>
        /// <returns></returns>
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

        
        /// <summary>
        /// Get a single section ID from canvas using the FIVE DIGIT IDENTIFIER for the canvas section in the gradebook
        /// </summary>
        /// <param name="courseID"></param>
        /// <param name="sectionNumber"></param>
        /// <returns>Canvas Section ID number</returns>
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
            
        
        /// <summary>
        /// Gets the most recent course ID
        /// Currently only works if you are a TA for one course
        /// </summary>
        /// <returns>Course ID number</returns>
        /// TODO: change this method's name, make it abstract
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

        
        /// <summary>
        /// Given path to assignment CSV, creates list of student objects with zybooks CSV data
        /// </summary>
        /// <param name="Path to CSV"></param>
        /// <returns>List of students</returns>
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