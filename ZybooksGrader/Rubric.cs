using System;
using System.Collections.Generic;

namespace ZybooksGrader {
    public class Rubric {
        public List<Criterion> criteria = new List<Criterion>();
        public string id;
        public class Criterion {
            public List<Rating> ratings = new List<Rating>();
            public string id;
            
            public class Rating {
                public string id;
                public Decimal points;
            }
        }

    }
}