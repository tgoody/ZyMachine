using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ZybooksGrader {
    class Program {

        //should probably make stuff like this in an xml file
        
        private static string assignmentName = "Lab 7";
        
        private static string token;
        private static string canvasAPIurl = "https://ufl.beta.instructure.com/api/v1/"; 
        private static HttpClient webber = new HttpClient();
        private static Dictionary<string, string> fixedNames = new Dictionary<string, string>();
        private static bool needUserPrompts = false;
        private static bool gradeAllSections = true;
        private static bool gradeWithRubric = false;
        private static bool excludeSections = false;
        private static List<string> excludes = new List<string>();
        private static string csvPath = "SomePath";
        private static bool overwriteGrades = true;

        static void Main() {
            realMain().GetAwaiter().GetResult();
        }
        
        public static async Task realMain() {
            StreamReader file = new StreamReader("Token.txt");
            token = file.ReadLine();
            webber.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            try {
                file = new StreamReader("FixedNames.txt");
                string fixedNamesLine;
                while ((fixedNamesLine = file.ReadLine()) != null) {
                    var names = fixedNamesLine.Split(',');
                    fixedNames.Add(names[0].ToLower(), names[1].ToLower());
                }
            }
            catch (Exception e) {
                Console.WriteLine("Fixed Names file either missing or empty, is this right?");
            }

            string courseCode;
            if (needUserPrompts) {
                var courses = await GetCoursesThisSemester();
                List<string> courseNames = courses.Keys.ToList();
                Console.WriteLine("***Which course are you choosing? (enter number, 0 indexed)");
                int tempCount = 0;
                foreach (var name in courseNames) {
                    Console.WriteLine($"{tempCount++}. {name}");
                }
                string indexChoice = Console.ReadLine();
                courseCode = courses[courseNames[Int32.Parse(indexChoice)]];
            }
            else {
                courseCode = await getMostRecentCourseCode();
            }
            Console.WriteLine($"Course code: {courseCode}");
            
            if (needUserPrompts) {
                Console.WriteLine("***Will you be grading with a Zybooks rubric or your own?");
                Console.WriteLine("0. Zybooks\n1. My own");
                string userChoice = Console.ReadLine();
                if (userChoice == "0") {
                    gradeWithRubric = false;
                }
                else if (userChoice == "1") {
                    gradeWithRubric = true;
                }
                else {
                    throw new Exception("bad input, handle this later");
                }
            }
            
            List<string> sectionIDs = await getSectionIDs(courseCode);
            Console.WriteLine($"{sectionIDs.Count} sections found");

            if (needUserPrompts) {
                Console.WriteLine("***Type some part of the title of your assignment - if it's \"Python Pitches\", type \"Pitches\"");
                assignmentName = Console.ReadLine();
            }
            
            var assignmentID = await getAssignmentIDbyName(courseCode, assignmentName);
            Console.WriteLine($"Found assignment ID: {assignmentID}");
            
            
            Dictionary<string, string> userIDs = new Dictionary<string, string>();
            if (needUserPrompts) {
                Console.WriteLine("***Will you be excluding sections?");
                Console.WriteLine("0. No\n1. Yes");
                string userChoice = Console.ReadLine();
                if (userChoice == "0") {
                    excludeSections = false;
                }
                else if (userChoice == "1") {
                    excludeSections = true;
                    Console.WriteLine("Input all the section numbers (5 digits) listed, separated by commas, then press enter.");
                    string listOfSections = Console.ReadLine();
                    var secArr = listOfSections.Split(',');
                    excludes.AddRange(secArr);
                }
                else {
                    throw new Exception("bad input, handle this later");
                }
            }

            if (excludeSections) {
                //should have some sort of list of this in an xml
                excludes.Add(getSectionID(courseCode, "11088").Result);
                excludes.Add(getSectionID(courseCode, "11091").Result);
            }
            
            if (gradeAllSections) {
                foreach (var section in sectionIDs) {
                    if (excludeSections && excludes.Contains(section)) {
                        continue;
                    }
                    await getUserIDsBySection(courseCode, section, userIDs);
                }
            }
            
            else {
                var mySectionID = getSectionID(courseCode, "13034").Result; 
                await getUserIDsBySection(courseCode, mySectionID, userIDs);
            }

            if (needUserPrompts) {
                Console.WriteLine("***Please input path of CSV file:");
                csvPath = Console.ReadLine();
            }
            
            List<Student> students;
            if (gradeWithRubric) {
                students = CreateFerbyStudents(csvPath);
                var rubricId = await GetRubricID(courseCode, assignmentID);
                var rubricFormat = await GenerateRubric(courseCode, rubricId);
                await FerbyTask(courseCode, userIDs, assignmentID, students, rubricFormat);
            }
            else {
                students = convertZybooksCSV(csvPath);

                
                //use to generate bad names for students here
                await GenerateBadNames(students, userIDs);
                
                
                
                
                // int temp = await updateGrades(courseCode, students, userIDs, assignmentID);
                // Console.WriteLine($"Finished with {temp} students");
            }
        }


        /// <summary>
        /// Used to generate a text file named "BadNames.txt" containing mismatched student names between zybooks and canvas
        /// </summary>
        /// <param name="studentsFromFile">Zybooks List</param>
        /// <param name="canvasStudents">Students from Canvas in Dictionary format</param>
        public static async Task GenerateBadNames(List<Student> studentsFromFile,
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
                        if (!overwriteGrades) {
                            continue;
                        }
                    }

                    Dictionary<string, string> temp = new Dictionary<string, string>();
                    temp.Add("submission[posted_grade", Convert.ToString(student.grade));
                    var currDate = DateTime.Now.Date;
                    var comment = $"Autograded on: {currDate:d}\nIf this grade is incorrect, please contact your TA.";
                    temp.Add("comment[text_comment]", comment);
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
                try {
                    results.Add(((string) student["name"]).ToLower(), (string) student["id"]);
                }
                catch (Exception e) {
                    Console.WriteLine(e.Message);
                }

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

            int lastNameIndex = 0, firstNameIndex = 1, emailIndex = 2, percentageIndex = 5, gradeIndex = 6, labTotal = 9;
            int labGrade = 0;

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
                else if (headerName.Contains("lab total")) {
                    labTotal = i;
                    var firstParen = dataPoints[i].IndexOf('(');
                    var secondParen = dataPoints[i].IndexOf(')');
                    var subStr = dataPoints[i].Substring(firstParen+1, secondParen - firstParen-1);
                    labGrade = Convert.ToInt32(subStr);
                }
            }

            List<Student> students = new List<Student>();
  
            while ((line=file.ReadLine()) != null) {

                dataPoints = line.Split(',');
                Student newStudent = new Student();

                newStudent.lastName = dataPoints[lastNameIndex];
                newStudent.firstName = dataPoints[firstNameIndex];
                newStudent.email = dataPoints[emailIndex];
                newStudent.percentage = Convert.ToDecimal(dataPoints[percentageIndex]);
                var studentGrade = Convert.ToDecimal(dataPoints[labTotal]);
                studentGrade /= 100;
                studentGrade *= labGrade;
                studentGrade = Math.Round(studentGrade, MidpointRounding.AwayFromZero);
                //newStudent.grade = Convert.ToDecimal(dataPoints[gradeIndex]);
                newStudent.grade = studentGrade;
                students.Add(newStudent);
            }
            return students;
        }
        
        
        /// <summary>
        /// Gets dictionary of course names as keys and their IDs as values
        /// </summary>
        static async Task<Dictionary<string, string>> GetCoursesThisSemester() {
        
            var response = webber.GetAsync(canvasAPIurl + "courses?enrollment_type=ta").Result;
            var content = await response.Content.ReadAsStringAsync();
            var results = new Dictionary<string, string>();   
        
            JArray obj = JsonConvert.DeserializeObject<JArray>(content);
            int enrollment_term_id = (int) obj[0]["enrollment_term_id"];
            foreach (JToken res in obj)
            {
                if (enrollment_term_id < (int)res["enrollment_term_id"]) {
                    enrollment_term_id = (int) res["enrollment_term_id"];
                }
            }
            foreach (JToken res in obj)
            {
                if ((int)res["enrollment_term_id"] == enrollment_term_id) {
                    results.Add((string)res["name"], (string)res["id"]);
                }
            }
            return results;
        }
        
        
    public static async Task<string> GetRubricID(string courseID, string assessmentID) {
        var sectionURI = canvasAPIurl + $"/courses/{courseID}/assignments/{assessmentID}";
        var response = webber.GetAsync(sectionURI).Result;
        var content = await response.Content.ReadAsStringAsync();
        JObject jsonData = JsonConvert.DeserializeObject<JObject>(content);
        try {
            JToken rubricSettings = jsonData["rubric_settings"];
            return (string) rubricSettings["id"];
        }
        catch (Exception e) {
            Console.WriteLine(e.Message);
            return null;
        }
    }

    public static async Task<Rubric> GenerateRubric(string courseID, string rubricId) {
        
        Rubric resultRubric = new Rubric();
        resultRubric.id = rubricId;
        var sectionURI = canvasAPIurl + $"/courses/{courseID}/rubrics/{rubricId}";
        var response = webber.GetAsync(sectionURI).Result;
        var content = await response.Content.ReadAsStringAsync();
        JObject jsonData = JsonConvert.DeserializeObject<JObject>(content);
        JArray criteriaData = (JArray)jsonData["data"];
        foreach (JToken criterion in criteriaData) {
            Rubric.Criterion tempCriterion = new Rubric.Criterion();
            tempCriterion.id = (string)criterion["id"];
            JArray ratingData = (JArray) criterion["ratings"];
            
            foreach (JToken rating in ratingData) {
                Rubric.Criterion.Rating tempRating = new Rubric.Criterion.Rating();
                tempRating.id = (string)rating["id"];
                tempRating.points = (Decimal) rating["points"];
                tempCriterion.ratings.Add(tempRating);
            }
            
            resultRubric.criteria.Add(tempCriterion);
        }

        return resultRubric;

    }

    public static List<Student> CreateFerbyStudents(string csvPath) {
        List<Student> students = new List<Student>();
        StreamReader file = new StreamReader(csvPath);
        string line = file.ReadLine();
        int lastNameIndex = 0, firstNameIndex = 1;
        int commentIndex = line.Split(',').Length - 1;

        while ((line=file.ReadLine()) != null) {

            var dataPoints = line.Split(',');
            Student newStudent = new Student();
            newStudent.rubricGrades = new List<decimal>();
            newStudent.lastName = dataPoints[lastNameIndex];
            newStudent.firstName = dataPoints[firstNameIndex];
            for (int i = 2; i < commentIndex; i++) {
                try {
                    newStudent.rubricGrades.Add(Convert.ToDecimal(dataPoints[i]));
                }
                catch (Exception e) {
                    if (dataPoints[i] == "") {
                        newStudent.rubricGrades.Add(0);
                    }
                }
            }

            if(dataPoints.Length - commentIndex > 0) {
                newStudent.comment = String.Join(",", dataPoints, commentIndex, dataPoints.Length - commentIndex);
            }
            students.Add(newStudent);
        }
        return students;
    }

    public static async Task FerbyTask(string courseID, Dictionary<string, string> canvasStudents,
        string assessmentID, List<Student> studentsFromFile, Rubric rubric) {

        int counter = 0;
        foreach (var student in studentsFromFile) {

            string studentName = (student.firstName + " " + student.lastName).ToLower();

            if (canvasStudents.ContainsKey(studentName)) {

                var gradeURI = canvasAPIurl +
                               $"courses/{courseID}/assignments/{assessmentID}/submissions/{canvasStudents[studentName]}";

                var response = await webber.GetAsync(gradeURI);
                var content = await response.Content.ReadAsStringAsync();
                JObject jsonData = JsonConvert.DeserializeObject<JObject>(content);

                var stringCheck = jsonData["grade"].ToString();

                if (stringCheck != "") {
                    Console.WriteLine("Grade not null");
                    if (!overwriteGrades) {
                        continue;
                    }
                }
                Dictionary<string, string> temp = new Dictionary<string, string>();
                List<string> listOfQueries = new List<string>();
                if (student.rubricGrades.Count != rubric.criteria.Count) {
                    throw new Exception("mismatch of criteria count");
                }

                for (int i = 0; i < rubric.criteria.Count; i++) {
                    var criterion = rubric.criteria[i];
                    temp.Add($"rubric_assessment[{criterion.id}][points]", Convert.ToString(student.rubricGrades[i]));
                    listOfQueries.Add($"rubric_assessment[{criterion.id}][points]={student.rubricGrades[i]}");
                    string ratingIDChosen = "";
                    for (int j = 0; j < criterion.ratings.Count; j++) {
                        if (criterion.ratings[j].points <= student.rubricGrades[i]) {
                            ratingIDChosen = criterion.ratings[j].id;
                            break;
                        }
                    }
                    temp.Add($"rubric_assessment[{criterion.id}][rating_id]", ratingIDChosen);
                    //listOfQueries
                }
                if (!string.IsNullOrEmpty(student.comment)) {
                    temp.Add("comment[text_comment]", student.comment);
                }
                var payload = new FormUrlEncodedContent(temp);
                response = await webber.PutAsync(gradeURI, payload);
                Console.WriteLine($"{++counter} students graded");
            }
        }
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