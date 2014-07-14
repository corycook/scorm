using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.InteropServices.ComTypes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Scorm;

namespace TestScorm
{
    [TestClass]
    public class TestDataSet
    {
        private abstract class TestDataModel : DataModel<TestDataModel>
        {
            public int Id { get; set; }

            protected TestDataModel() : base("") { }
        }

        [TestMethod]
        public void TestClone()
        {
            var property = typeof(DataSet<TestDataModel>).GetProperty("SearchExpressions", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(property);

            var testSet = new DataSet<TestDataModel>("");
            var result = property.GetValue(testSet, null) as ICollection<Expression>;
            Expression<Func<TestDataModel, bool>> expression = model => model.Id == 1;
            Assert.IsNotNull(result);
            result.Add(expression);
            Assert.IsTrue(result.Count == 1);

            var clone = testSet.Clone() as DataSet<TestDataModel>;
            Assert.IsNotNull(clone);
            var result2 = property.GetValue(clone) as ICollection<Expression>;
            Assert.IsNotNull(result2);
            Assert.IsTrue(result2.Count == result.Count);

            var whereRes = clone.Where(n => n.Id == 3);
            Assert.IsNotNull(whereRes);
            var result3 = property.GetValue(whereRes) as ICollection<Expression>;
            Assert.IsNotNull(result3);
            Assert.IsTrue(result3.Count != result2.Count);
        }
    }
}
