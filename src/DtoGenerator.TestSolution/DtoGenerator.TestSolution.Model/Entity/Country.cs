﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DtoGenerator.TestSolution.Model.Infrastructure;

namespace DtoGenerator.TestSolution.Model.Entity
{
    public class Country : EntityBase
    {
        public string Name { get; set; }
        public string Code { get; set; }
    }
}
