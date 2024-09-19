# DynamicAgent
This project allows serializing a method to a string, so it can then be encrypted / sent over the network / executed in another program.  
  
For example of how to export a method to a JSON string, and how to take a JSON string and execute a method from it - check out the 'Program.cs' file.  

This project contains 2 classes:
* **GenerateMethod** - Implements logic to take all the data of a method, parse it to dedicated structs, and serialize the structs to a JSON string.
* **DynamicExecute** - Implements logic to deserialize the JSON string to the same dedicated structs, build a DynamicMethod from this information, resolve members used in the method and execute it.

And 2 structs:
* **Method** - Contains information relevant for building and executing a method, such as declarations, parameter types, local variables in the method and more.
* **InlineTokenInfo** - Contains information about a metadata token used inside the method, such as it's index in the method's IL stream, what member it should be resolved to and it's parameters and types.
  
**Note:** This is a little PoC to demonstrate the concept of DynamicMethods, and it probably doesn't cover all edge cases when converting a regular method to a DynamicMethod.  
Feel free to edit this and update on issues you find!

## Exporting methods
To export a method, we need to use the *GenerateMethod* class, which has 2 methods to do this:

* **Generate** -  To get a 'Method' struct representation of the method.
* **GenerateMethodAsJSON** - To get a JSON string of the method implementation.

For example, to export the method "CalculateMD5", and to execute later with the parameters: [ @"C:\Users\Asif\Desktop\GokuSupreme.jpg", 0x2D, "" ], we'll do this:
```C#
MethodInfo info = typeof(Program).GetMethod("CalculateMD5");
object[] parameters = new object[] { @"C:\Users\Asif\Desktop\GokuSupreme.jpg", 0x2D, "" };
string methodJson  = GenerateMethod.GenerateMethodAsJSON(info, parameters, true);
```

## Execute methods
As simple as it was to export a method, it is that easy to execute one from the JSON/Method-struct we exported. All we need to do is to use the *DynamicExecute* class, which has a method to do this, with 2 overloads corresponding to the *GenerateMethod* methods:

* **ExecuteMethod(Method method)** - Builds a DynamicMethod from the Method struct it accepts.
* **ExecuteMethod(string methodAsJson)** - Builds a DynamicMethod from the JSON string it accepts.

For example:
```C#
string s = (string)DynamicExecute.ExecuteMethod(methodJson);
```
This method returns and *object*, but we know the method we currently execute ("CalculateMD5") returns a string, so we cast it.

# Understanding Dynamic Methods

If you want to enrich your knowledge about .NET in general, and DyanmicMethods in particular, you're welcome to read my blogposts:

// Posts links
