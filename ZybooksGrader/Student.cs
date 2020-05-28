using System;
using System.Collections.Generic;

namespace ZybooksGrader {
    public class Student {

        public string lastName;
        public string firstName;
        public string email;
        public Decimal grade;
        public Decimal percentage;
        public List<Decimal> rubricGrades = null;
        public string comment;

    }
}