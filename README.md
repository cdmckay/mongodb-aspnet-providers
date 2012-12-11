ASP.NET Providers for MongoDB
=============================

## Overview

This implementation of the [ASP.NET 2.0 Providers](http://msdn.microsoft.com/en-us/library/aa478948.aspx) includes the following providers:

* [Membership Provider](http://msdn.microsoft.com/en-us/library/system.web.security.membershipprovider.aspx)
* [Role Provider](http://msdn.microsoft.com/en-us/library/system.web.security.roleprovider.aspx)
* [Profile Provider](http://msdn.microsoft.com/en-us/library/system.web.profile.profileprovider.aspx)
* [Session State Provider](http://msdn.microsoft.com/en-us/library/system.web.sessionstate.sessionstatestoreproviderbase.aspx)

This implementation does NOT include the following providers:

* Site Map Provider
* Web Event Provider
* Web Parts Personalization Provider

These providers are also available [via NuGet](http://nuget.org/List/Packages/DigitalLiberationFront.MongoDB.Web).

## ASP.NET Web Site Configuration Tool Issue

### Problem

If you use the ASP.NET Web Site Administration Tool (WSAT), you might have noticed that, after you add a new user, the Security tab will stop working, giving the error:

> Type is not resolved for member 'MongoDB.Bson.ObjectId,MongoDB.Bson, Version=1.1.0.4184, Culture=neutral, PublicKeyToken=f686731cfb9cc103'.

### Cause

From what I can gather from the web, this is caused by how the WSAT invokes the provider code. Basically, if your provider uses a non-GAC type for MembershipUser provider key, then WSAT can't serialize it because it can't find the class.

### Solution

You will need to install the `MongoDB.Bson.dll` to the GAC. Please note you only have to do this if you want to use the WSAT.

We need to use `gacutil` to install the `MongoDB.Bson.dll` to the GAC. Visual Studio 2010 comes with it and automatically adds it to the PATH if you use the Visual Studio Command Prompt. So here we go:

1. Go to Start - All Programs - Microsoft Visual Studio 2010 - Visual Studio Tools - Visual Studio Command Prompt (2010) but DO NOT left-click. Instead, right-click and select "Run as administrator".
2. From the command prompt, change to the directory containing `MongoDB.Bson.dll`. If you're using NuGet, that will be in `MongoDB.Web.Sample\packages`.
3. From that directory, `run gacutil -i MongoDB.Bson.dll`. It should print `Assembly successfully added to the cache`.
4. Ensure that all the ASP.NET Development Server running WSAT is shut down.
5. Rebuild your project.
6. Restart the WSAT by clicking the "ASP.NET Configuration" button in Visual Studio 2010.

The WSAT should now be working correctly.

