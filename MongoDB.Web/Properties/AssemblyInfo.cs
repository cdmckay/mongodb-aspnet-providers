using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

// General Information about an assembly is controlled through the following 
// set of attributes. Change these attribute values to modify the information
// associated with an assembly.
[assembly: AssemblyTitle("DLF ASP.NET Providers for MongoDB")]
[assembly: AssemblyDescription("MongoDB ASP.NET Providers including Membership, Role, Profile and Session State Providers.")]
[assembly: AssemblyConfiguration("")]
[assembly: AssemblyCompany("Cameron McKay")]
[assembly: AssemblyProduct("DLF ASP.NET Providers for MongoDB")]
[assembly: AssemblyCopyright("Copyright © Cameron McKay 2011")]
[assembly: AssemblyTrademark("")]
[assembly: AssemblyCulture("")]

// Setting ComVisible to false makes the types in this assembly not visible 
// to COM components.  If you need to access a type in this assembly from 
// COM, set the ComVisible attribute to true on that type.
[assembly: ComVisible(false)]

// The following GUID is for the ID of the typelib if this project is exposed to COM
[assembly: Guid("82fc7779-9909-4cc8-bb83-d6851ae0343b")]

// Version information for an assembly consists of the following four values:
//
//      Major Version
//      Minor Version 
//      Build Number
//      Revision
//
// You can specify all the values or you can default the Build and Revision Numbers 
// by using the '*' as shown below:
// [assembly: AssemblyVersion("1.0.*")]
[assembly: AssemblyVersion("1.0.0.0")]

// Make some internal classes available to the Test project.
[assembly: InternalsVisibleTo("DigitalLiberationFront.MongoDB.Web.Test")]
