using System;

namespace GHErrorAssistant
{
    // Uses a framework type descriptor, never GH_Param<T>.TypeName, which can instantiate T.
    internal static class ParameterTypeMetadata
    {
        internal static string Describe(Func<Type> getType)
        {
            try
            {
                var type = getType();
                if (type == null || type.IsInterface || type.IsAbstract) return "";
                // Only known SDK goo types have a trustworthy beginner-facing label.
                if (type.Namespace != "Grasshopper.Kernel.Types") return "";
                switch (type.Name)
                {
                    case "GH_Curve": return "Curve";
                    case "GH_Point": return "Point";
                    case "GH_Surface": return "Surface";
                    case "GH_Mesh": return "Mesh";
                    case "GH_Brep": return "Brep";
                    case "GH_Number": return "Number";
                    case "GH_Integer": return "Integer";
                    case "GH_Boolean": return "Boolean";
                    case "GH_String": return "Text";
                    case "GH_Vector": return "Vector";
                    default: return "";
                }
            }
            catch (Exception) { return ""; }
        }
    }
}

