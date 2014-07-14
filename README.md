scorm
=====

SCORM is SQL Conversion and Object Relational Mapping

 

The two aspects of SCORM are the DataSet and the DataModel.

 

DataModel

The DataModel provides the interface to the data table stored in the SQL Server database.

 

Rules:

1.       The name of the DataModel class must match the name of the data table on the database. If the data table name cannot transpose properly to a class name then you must use the Table Name Attribute (Scorm.TableNameAttribute).

2.       The name of each property in the DataModel must match the name of the fields in the database that you wish to pull.

3.       The key fields must be mapped and the Key Attribute (System.ComponentModel.DataAnnotations.KeyAttribute) must be applied to each key.

4.       The DataModel class must inherit the generic DataModel and pass itself as the generic type (example below)

5.       The DataModel must implement a default constructor that passes a connection string to the base constructor. The DataModel may implement additional constructors to pass other connection strings.

 

Example:



using System.ComponentModel.DataAnnotations;

using Scorm;

 

[TableName("Customer Shipping Locations")]

    public class Customer_Shipping_Location : DataModel<Customer_Shipping_Location>

    {

        [Key]

        public int Id { get; set; }

        public int Shipping_Locations_Id { get; set; }

        public string Customer_Id { get; set; }

        public Customer_Shipping_Location() : base(Utility.ConnectionString("ShippingInformationConnectionString")) { }

    }
 

 

Since the table name has spaces we must use the TableNameAttribute to apply the proper table name. Id is the only unique key so we apply the KeyAttribute only to that property.

 

Functionality:



Find
 
[static] Find queries the database based on key values
 
Customer_Shipping_Locations.Find(new { Id = 3 });
 

Find
 
[static] Overloaded Find queries objects with a single string key without creating an object.
 
Customer_Shipping_Locations.Find("3"); // type error
 

TryFind
 
[static] Similar to find except will return true/false instead of throwing an error.
 
Customer_Shipping_Locations result;

if (!Customer_Shipping_Locations.TryFind(new { Id = 3 }), out result)) Response.Write("Error! Not Found");
 

Insert
 
[instance] Insert allows you to insert a new object into the database.
 
var inserted = new Customer_Shipping_Locations { Shipping_Locations_Id = 2, Customer_Id = cusid };
inserted.Insert();
 

Update
 
[instance] Update allows you to update an object pulled from the database.
 
var csl = Customer_Shipping_Locations.Find(new { Id = 3 });
csl.Shipping_Locations_Id = 2;
csl.Update();
 

Delete
 
[instance] Delete allows you to delete an object pulled from the database.
 
var csl = Customer_Shipping_Locations.Find(new { Id = 3 });

csl.Delete();
 

 

DataSet

The DataSet provides a query system to interface with the database. DataSet implements the IEnumerable<T> interface so you can pull data using Linq methods. Linq's ToList, ToArray, etc. will pull all data matching the current query state. Linq's Select method will be useful for casting DataModels to different types.

 

Rules:

1.       DataSet must be instantiated using a DataModel as the generic type.

2.       DataSet constructor requires a connectionString to be passed.

 

Example:



var set = new DataSet<Customer_Shipping_Locations>(connectionString);
 

 

It may be useful to represent a database as an objects with a collection of DataSets. e.g.

 



public class ShippingLocationInformation

    {

        public DataSet<Shipping_Locations> ShippingLocations { get; private set; }

        public DataSet<Customer_Shipping_Location> CustomerShippingLocations { get; private set; }

 

        private const string ConnectionStringName = "ShippingInformationConnectionString";

        public ShippingLocationInformation()

        {

            foreach (var i in GetType().GetProperties())

            {

                i.SetValue(this, Activator.CreateInstance(i.PropertyType, new object[] 

              { Utility.ConnectionString(ConnectionStringName) }), null);

            }

        }

    }
 

 

This example uses reflection to set all of the DataSets to new instances using the same connection string.

 

You can then instantiate the database class and use the database properties to reference the DataSets.

 



var _db = new ShippingLocationInformation();
var set = _db.CustomerShippingLocations;
 

 

Functionality:

Where

The instance method Where allows you to query the database using Linq expressions. This allows the programmer to utilize the functionality of Visual Studio to assist the developer in the construction and analysis of database queries. This will also allow the compiler to double check the logic behind the queries to ensure that they are correct. Calling Where returns a new DataSet with the filter implemented.

 



set = set.Where(model => model.Customer_Id == "demo");
 

 

Model represents the record in question (the name "model" is local to the anonymous expression and can be defined by the programmer). The anonymous expression accepts a record instance and returns a Boolean value indicating whether or not the record should be included in the set.

 

Where recognizes the following expression types:

==, >, <, >=, <=, !=, ||, &&, ()

 

Where also recognizes the following string queries:

Contains, StartsWith, EndsWith

 



set = set.Where(model => model.Customer_Id.Contains("demo"));
 

 

An example of a more complex query could be:



set = set.Where(n => n.Customer_Id.Contains("demo") || (n.Id >= 500 && n.Shipping_Locations_Id < 3);
 

 

Calling where multiple times is equivalent to using '&&' in the query.

 

Pitfalls:

You can use property references, local variables, etc. in queries; however, the system cannot tell the difference between the same property on different instances. For instance,



var customer = Customer_Shipping_Locations.Find(new { Id = 3 });
set = set.Where(model => model.Id == customer.Id);
 

will not work properly since "customer" is the same type as the object being queried.

 

In order to work around this save the value into a local variable prior to querying:



var customer = Customer_Shipping_Locations.Find(new { Id = 3 });

var id = customer.Id;
set = set.Where(model => model.Id == id);
 

 

The system does not currently parse method calls. So any method call result should be done prior to the query and referenced as a local variable.

 

OrderBy and OrderByDescending

The OrderBy and OrderByDescending methods use Linq expressions similar to Where except that they must only reference a single property.

 



set = set.OrderBy(n => n.Customer_Id)
 

 

Calling OrderBy multiple times allows sorting on multiple properties; however, it does not check to make sure that you aren't already sorting on a property. So if you call OrderBy or OrderByDescending for the same property multiple times the system will throw a SQL Exception when it tries to pull data.

 

Max and Min

The Max and Min instance methods will get the appropriate value for the specified property given the current query state.

 



var maxid = set.Max(n => n.Id);
 

 

Count

The Count property returns the number of records that matches the current query state.

 

 

Since DataSets return DataModels you can use DataSet queries to Delete or Update multiple records simultaneously.

Since DataSets implement the IEnumerable<T> interface you can use DataSets as targets of foreach blocks:



foreach (var i in set) {

    i.Customer_Id = "demo";

    i.Update();
}
 
