using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Reflection;

namespace Scorm
{
    public abstract class DataModel
    {
        public readonly SqlConnection Connection = new SqlConnection();
        private readonly SqlCommand _command;
        private IEnumerable<PropertyInfo> Properties { get { return GetType().GetProperties(); } }
        private IEnumerable<PropertyInfo> Keys { get { return Properties.Where(n => Attribute.IsDefined(n, typeof(KeyAttribute))); } }

        private string TableName
        {
            get
            {
                var result = GetType().GetCustomAttributes(typeof(TableNameAttribute), false);
                return result.Any() ? ((TableNameAttribute)result.First()).Name : GetType().Name;
            }
        }

        protected DataModel(string connectionString)
        {
            Connection.ConnectionString = connectionString;
            _command = Connection.CreateCommand();
        }

        public DataModel Apply(DataRow row)
        {
            foreach (var i in GetType().GetProperties())
            {
                i.SetValue(this, row[i.Name] != DBNull.Value ? row[i.Name] : null, null);
            }
            return this;
        }

        public DataModel Apply(IDataReader row)
        {
            foreach (var i in GetType().GetProperties())
            {
                i.SetValue(this, row[i.Name] != DBNull.Value ? row[i.Name] : null, null);
            }
            return this;
        }

        public void Insert()
        {
            var inserted = Properties.Where(n => n.GetValue(this, null) != null || Attribute.IsDefined(n, typeof(DefaultValueAttribute))).ToArray();
            _command.CommandText = String.Format("INSERT INTO [{0}] ({1}) VALUES ({2})", TableName,
                String.Join(", ", inserted.Select(n => string.Format("[{0}]", n.Name))),
                String.Join(", ", inserted.Select(n => string.Format("@{0}", n.Name))));
            foreach (var i in inserted)
            {
                _command.Parameters.AddWithValue(i.Name, i.GetValue(this, null) ??
                    ((DefaultValueAttribute)i.GetCustomAttributes(typeof(DefaultValueAttribute), false).First()).Value);
            }
            Connection.Open();
            _command.ExecuteNonQuery();
            Connection.Close();
        }

        public void Update()
        {
            var updated = Properties.Where(n => !Attribute.IsDefined(n, typeof(KeyAttribute)));
            if (Keys.Any(n => n.GetValue(this, null) == null)) throw new ArgumentNullException();
            _command.CommandText = String.Format("UPDATE [{0}] SET {1} WHERE {2}", GetType().Name,
                String.Join(", ", updated.Select(n => string.Format("[{0}] = @{0}", n.Name))),
                String.Join(" AND ", Keys.Select(n => string.Format("[{0}] = @_{0}", n.Name))));
            foreach (var i in Properties)
            {
                var val = (i.GetValue(this, null) ?? DBNull.Value).ToString().Trim();
                _command.Parameters.AddWithValue(string.Format("{0}{1}", Attribute.IsDefined(i, typeof(KeyAttribute)) ? "_" : string.Empty, i.Name), val);
            }
            Connection.Open();
            _command.ExecuteNonQuery();
            Connection.Close();
        }

        public void Delete()
        {
            if (Keys.Any(n => n.GetValue(this, null) == null)) throw new ArgumentNullException();
            _command.CommandText = String.Format("DELETE FROM {0} WHERE {1}", GetType().Name,
                String.Join(" AND ", Keys.Select(n => string.Format("{0} = @{0}", n.Name))));
            foreach (var i in Keys)
            {
                _command.Parameters.AddWithValue(i.Name, i.GetValue(this, null).ToString());
            }
            Connection.Open();
            _command.ExecuteNonQuery();
            Connection.Close();
        }
    }

    public class DataModel<T> : DataModel where T : DataModel
    {
        protected DataModel(string connection) : base(connection) { }

        public static T Find(string id, string connectionString = null)
        {
            connectionString = connectionString ?? Activator.CreateInstance<T>().Connection.ConnectionString;
            return Find(GetCommand(id, connectionString), connectionString);
        }

        public static T Find(object d, string connectionString = null)
        {
            connectionString = connectionString ?? Activator.CreateInstance<T>().Connection.ConnectionString;
            return Find(GetCommand(d, connectionString), connectionString);
        }

        private static T Find(SqlCommand command, string connectionString)
        {
            using (var adapter = new SqlDataAdapter(command))
            {
                var set = new DataSet();
                adapter.Fill(set);
                if (set.Tables.Count <= 0 || set.Tables[0].Rows.Count <= 0)
                    throw new Exception(String.Format("key not found on {0}", typeof(T).Name));
                var result = Activator.CreateInstance<T>();
                result.Connection.ConnectionString = connectionString;
                result.Apply(set.Tables[0].Rows[0]);
                return result;
            }
        }

        public static bool TryFind(string id, out T result, string connectionString = null)
        {
            connectionString = connectionString ?? Activator.CreateInstance<T>().Connection.ConnectionString;
            return TryFind(GetCommand(id, connectionString), connectionString, out result);
        }

        public static bool TryFind(object d, out T result, string connectionString = null)
        {
            connectionString = connectionString ?? Activator.CreateInstance<T>().Connection.ConnectionString;
            return TryFind(GetCommand(d, connectionString), connectionString, out result);
        }

        private static bool TryFind(SqlCommand command, string connectionString, out T result)
        {
            try
            {
                result = Find(command, connectionString);
                return true;
            }
            catch
            {
                result = null;
                return false;
            }
        }

        private static string GetTableName()
        {
            var attr = typeof(T).GetCustomAttributes(typeof(TableNameAttribute), false);
            return attr.Any() ? ((TableNameAttribute)attr.First()).Name : typeof(T).Name;
        }

        private static SqlCommand GetCommand(string id, string connectionString)
        {
            var connection = new SqlConnection(connectionString);
            var command = connection.CreateCommand();
            var properties = typeof(T).GetProperties();
            var key = properties.First(n => Attribute.IsDefined(n, typeof(KeyAttribute)));
            if (key == null) throw new Exception("No keys defined for " + typeof(T).Name);
            command.CommandText = string.Format("SELECT {0} FROM [{1}] WHERE {2} = @{2}",
                string.Join(", ", properties.Select(n => string.Format("[{0}]", n.Name))),
                GetTableName(), key.Name);
            command.Parameters.AddWithValue(key.Name, id);
            return command;
        }

        private static SqlCommand GetCommand(object d, string connectionString)
        {
            var connection = new SqlConnection(connectionString);
            var command = connection.CreateCommand();
            var properties = typeof(T).GetProperties();
            var keys = properties.Where(n => Attribute.IsDefined(n, typeof(KeyAttribute))).ToArray();
            if (!keys.Any()) throw new Exception("No keys defined for " + typeof(T).Name);
            command.CommandText = string.Format("SELECT {0} FROM [{1}] WHERE {2}",
                string.Join(", ", properties.Select(n => "[" + n.Name + "]")), GetTableName(),
                string.Join(" AND ", keys.Select(n => "[" + n.Name + "] = @" + n.Name)));
            foreach (var i in keys)
            {
                if (d.GetType().GetProperty(i.Name) == null)
                    throw new Exception(
                        String.Format("{0} needs to be defined for {1}.", i.Name, typeof(T).Name));
                command.Parameters.AddWithValue(i.Name, d.GetType().GetProperty(i.Name).GetValue(d, null).ToString());
            }
            return command;
        }
    }

    public class TableNameAttribute : Attribute
    {
        public string Name { get; private set; }

        public TableNameAttribute(string name)
        {
            Name = name;
        }
    }
}