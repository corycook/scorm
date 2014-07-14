using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data.SqlClient;
using System.Linq;
using System.Linq.Expressions;

namespace Scorm
{
    public class DataSet<T> : IDisposable, IEnumerable<T>, ICloneable where T : DataModel
    {
        private readonly SqlConnection _connection = new SqlConnection();
        private readonly SqlCommand _command;
        private ICollection<Expression> SelectExpressions { get; set; }
        private ICollection<Expression> SearchExpressions { get; set; }
        private ICollection<Expression> SortExpressions { get; set; }
        private ICollection<Expression> SortDescendingExpressions { get; set; }

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
            SelectExpressions = new Collection<Expression>();
            SearchExpressions = new Collection<Expression>();
            SortExpressions = new Collection<Expression>();
            SortDescendingExpressions = new Collection<Expression>();
        }

        public DataSet(DataSet<T> source)
        {
            ConnectionString = source.ConnectionString;
            _command = _connection.CreateCommand();
            SelectExpressions = new Collection<Expression>(source.SelectExpressions.ToList());
            SearchExpressions = new Collection<Expression>(source.SearchExpressions.ToList());
            SortExpressions = new Collection<Expression>(source.SortExpressions.ToList());
            SortDescendingExpressions = new Collection<Expression>(source.SortDescendingExpressions.ToList());
        }

        public DataSet<T> Sort<TResult>(Expression<Func<T, TResult>> expression)
        {
            var result = new DataSet<T>(this);
            if (result == null) throw new NullReferenceException();
            result.SortExpressions.Add(expression);
            return result;
        }

        public DataSet<T> SortDescending<TResult>(Expression<Func<T, TResult>> expression)
        {
            var result = new DataSet<T>(this);
            if (result == null) throw new NullReferenceException();
            result.SortDescendingExpressions.Add(expression);
            return result;
        }

        public DataSet<T> Where(Expression<Func<T, bool>> expression)
        {
            var result = new DataSet<T>(this);
            if (result == null) throw new NullReferenceException();
            result.SearchExpressions.Add(expression);
            return result;
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

        private string SerializeSql()
        {
            return string.Format("{0} FROM [{1}] {2} {3}", SelectString, TableName, SearchString, SortString).Trim();
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
                    return ParseConstantExpression(((ConstantExpression)expression).Value);
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

        IEnumerator IEnumerable.GetEnumerator()
        {
            _command.CommandText = SerializeSql();
            return new DataSetEnumerator(_command);
        }

        public object Clone()
        {
            return new DataSet<T>(this);
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

            object IEnumerator.Current
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

        [Flags]
        private enum StringMethod
        {
            Contains,
            StartsWith,
            EndsWith
        }
    }

}