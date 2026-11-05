using System;
using System.Collections.Generic;
using System.Globalization;

namespace GardenLayout;

public static partial class GardenText
{
    public static Func<string,string>? Resolve;
    public static string L(string key)=>Resolve?.Invoke(key)??Source[key];
    public static string F(string key,params object?[] values)=>string.Format(CultureInfo.CurrentCulture,L(key),values);
}

