﻿using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Linq.Expressions;

namespace Scorm
{
    public class DataSet<T> : IDisposable, IEnumerable<T> where T : DataModel
    {
        private readonly SqlConnection _connection = new SqlConnection();
        private readonly SqlCommand _command;
        private IList<Expression> SelectExpressions { get; set; }
        private IList<Expression> SearchExpressions { get; set; }
        private IList<Expression> SortExpressions { get; set; }
        private IList<Expression> SortDescendingExpressions { get; set; }

        private string ConnectionString
        {
            get
            {
                return _connection.ConnectionString;
            }
            set
            {
                _connection.ConnectionString = value;
            }
        }

        private string SearchString
        {
            get
            {
                return SearchExpressions.Any()
                    ? string.Format("WHERE {0}", string.Join(" AND ", SearchExpressions.Select(ParseSearchExpression)))
                    : string.Empty;
            }
        }

        private string SortString
        {
            get
            {
                return SortExpressions.Any() || SortDescendingExpressions.Any()
                    ? string.Format("ORDER BY {0}", string.Join(", ",
                        SortExpressions.Select(ParseSortExpression)
                            .Concat(SortDescendingExpressions.Select(n => ParseSortExpression(n) + " DESC"))))
                    : string.Empty;
            }
        }

        private static string TableName
        {
            get
            {
                var attr = typeof(T).GetCustomAttributes(typeof(TableNameAttribute), false);
                return attr.Any() ? ((TableNameAttribute)attr.First()).Name : typeof(T).Name;
            }
        }

        public DataSet(string connectionString)
        {
            ConnectionString = connectionString;
            _command = _connection.CreateCommand();
            SelectExpressions = new List<Expression>();
            SearchExpressions = new List<Expression>();
            SortExpressions = new List<Expression>();
            SortDescendingExpressions = new List<Expression>();
        }

        public DataSet<T> Sort<TResult>(Expression<Func<T, TResult>> expression)
        {
            SortExpressions.Add(expression);
            return this;
        }

        public DataSet<T> SortDescending<TResult>(Expression<Func<T, TResult>> expression)
        {
            SortDescendingExpressions.Add(expression);
            return this;
        }

        public DataSet<T> Where(Expression<Func<T, bool>> expression)
        {
            SearchExpressions.Add(expression);
            return this;
        }

        public TResult Max<TResult>(Expression<Func<T, TResult>> expression)
        {
            _connection.Open();
            var column = ((MemberExpression)expression.Body).Member.Name;
            _command.CommandText = string.Format("SELECT MAX([{0}]) {0} FROM [{1}] {2}", column, TableName, SearchString);
            var result = _command.ExecuteScalar();
            _connection.Close();
            return result != DBNull.Value ? (TResult)result : Activator.CreateInstance<TResult>();
        }

        public TResult Min<TResult>(Expression<Func<T, TResult>> expression)
        {
            _connection.Open();
            var column = ((MemberExpression)expression.Body).Member.Name;
            _command.CommandText = string.Format("SELECT MIN([{0}]) {0} FROM [{1}] {2}", column, TableName, SearchString);
            var result = _command.ExecuteScalar();
            _connection.Close();
            return result != DBNull.Value ? (TResult)result : Activator.CreateInstance<TResult>();
        }

        public List<T> ToList()
        {
            _command.CommandText = SerializeSql();
            IDbDataAdapter adapter = new SqlDataAdapter(_command);
            var result = new DataSet();
            adapter.Fill(result);
            return result.Tables[0].Rows.Count > 0
                ? (from DataRow d in result.Tables[0].Rows
                   let obj = Activator.CreateInstance<T>()
                   where (obj.Connection.ConnectionString = ConnectionString) != null
                   select obj.Apply(d)).Cast<T>().ToList()
                : new List<T>();
        }

        public void Dispose()
        {
            _connection.Dispose();
            _command.Dispose();
        }

        public IEnumerator<T> GetEnumerator()
        {
            _command.CommandText = SerializeSql();
            return new DataSetEnumerator(_command);
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            _command.CommandText = SerializeSql();
            return new DataSetEnumerator(_command);
        }

        private string SerializeSql()
        {
            return string.Format("{0} FROM [{1}] {2} {3}", SelectString, TableName, SearchString, SortString).Trim();
        }

        private string SelectString
        {
            get
            {
                return string.Format("SELECT {0}",
                    SelectExpressions.Any()
                    ? string.Join(", ", SelectExpressions.Select(ParseSelectExpression))
                    : string.Join(", ", typeof(T).GetProperties().Select(n => string.Format("[{0}]", n.Name))));
            }
        }
        private static string ParseSearchExpression(Expression expression)
        {
            switch (expression.NodeType)
            {
                case ExpressionType.Lambda:
                    return ParseSearchExpression(((LambdaExpression)expression).Body);
                case ExpressionType.MemberAccess:
                    return ParseMemberExpression((MemberExpression)expression);
                case ExpressionType.Constant:
                    return string.Format("{0}", ParseConstantExpression(((ConstantExpression)expression).Value));
                case ExpressionType.Equal:
                    return ParseCompareExpression((BinaryExpression)expression, "=");
                case ExpressionType.NotEqual:
                    return ParseCompareExpression((BinaryExpression)expression, "<>");
                case ExpressionType.LessThan:
                    return ParseCompareExpression((BinaryExpression)expression, "<");
                case ExpressionType.GreaterThan:
                    return ParseCompareExpression((BinaryExpression)expression, ">");
                case ExpressionType.LessThanOrEqual:
                    return ParseCompareExpression((BinaryExpression)expression, "<=");
                case ExpressionType.GreaterThanOrEqual:
                    return ParseCompareExpression((BinaryExpression)expression, ">=");
                case ExpressionType.OrElse:
                    return ParseCompareExpression((BinaryExpression)expression, "OR");
                case ExpressionType.AndAlso:
                    return ParseCompareExpression((BinaryExpression)expression, "AND");
                case ExpressionType.Call:
                    return ParseCallExpression((MethodCallExpression)expression);
                default:
                    throw new Exception("Cannot handle: " + expression.NodeType);
            }
        }

        private static string ParseConstantExpression(object value)
        {
            return string.Format(value.GetType().IsValueType ? "{0}" : "'{0}'", value);
        }

        private static string ParseCompareExpression(BinaryExpression expression, string comparator)
        {
            return string.Format("({0} {1} {2})", ParseSearchExpression(expression.Left), comparator,
                ParseSearchExpression(expression.Right));
        }

        private static string ParseMemberExpression(MemberExpression expression)
        {
            var properties = typeof(T).GetProperties();
            if (properties.Contains(expression.Member))
                return string.Format("[{0}]", expression.Member.Name);
            var getMember = Expression.Convert(expression, typeof(object));
            var getValue = Expression.Lambda<Func<object>>(getMember);
            var value = getValue.Compile().Invoke();
            return ParseConstantExpression(value);
        }

        private static string ParseSortExpression(Expression expression)
        {
            switch (expression.NodeType)
            {
                case ExpressionType.Lambda:
                    return ParseSortExpression(((LambdaExpression)expression).Body);
                case ExpressionType.MemberAccess:
                    return ParseMemberExpression((MemberExpression)expression);
                default:
                    throw new Exception("Cannot handle: " + expression.NodeType);
            }
        }

        private static string ParseCallExpression(MethodCallExpression expression)
        {
            StringMethod method;
            return Enum.TryParse(expression.Method.Name, out method)
                ? string.Format("{0} LIKE '{1}{2}{3}'", ParseSearchExpression(expression.Object),
                    (StringMethod.Contains | StringMethod.EndsWith).HasFlag(method) ? "%" : string.Empty,
                    ParseSearchExpression(expression.Arguments[0]).Trim('\''),
                    (StringMethod.Contains | StringMethod.StartsWith).HasFlag(method) ? "%" : string.Empty)
                : string.Empty;
        }

        private static string ParseSelectExpression(Expression expression)
        {
            switch (expression.NodeType)
            {
                case ExpressionType.MemberAccess:
                    return ParseMemberExpression((MemberExpression)expression);
                case ExpressionType.New:
                    return string.Empty;
                default:
                    throw new Exception("Cannot handle: " + expression.NodeType);
            }
        }

        private class DataSetEnumerator : IEnumerator<T>
        {
            private readonly SqlCommand _command;
            private SqlDataReader _reader;

            public DataSetEnumerator(SqlCommand command)
            {
                _command = command;
                _command.Connection.Open();
                _reader = command.ExecuteReader();
            }

            public T Current
            {
                get { return Activator.CreateInstance<T>().Apply(_reader) as T; }
            }

            public void Dispose()
            {
                _command.Connection.Close();
                _reader.Dispose();
            }

            object System.Collections.IEnumerator.Current
            {
                get { return Current; }
            }

            public bool MoveNext()
            {
                return _reader.Read();
            }

            public void Reset()
            {
                _reader = _command.ExecuteReader();
            }
        }
    }


    [Flags]
    public enum StringMethod
    {
        Contains,
        StartsWith,
        EndsWith
    }
}