using Grpc.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace GrpcNet.Transport
{
    public static class ServiceBinderExtension
    {
        public static void Bind<T>(this ServiceBinderBase binder, T? server = null) where T : class
        {
            var binderAttrib = typeof(T).GetCustomAttribute<BindServiceMethodAttribute>(true);
            if (binderAttrib is null) throw new InvalidOperationException("No " + nameof(BindServiceMethodAttribute) + " found");
            if (binderAttrib.BindType is null) throw new InvalidOperationException("No " + nameof(BindServiceMethodAttribute) + "." + nameof(BindServiceMethodAttribute.BindType) + " found");

            var method = binderAttrib.BindType.FindMembers(MemberTypes.Method, BindingFlags.Public | BindingFlags.Static,
                static (member, state) =>
                {
                    if (member is not MethodInfo method) return false;
                    if (method.Name != (string)state!) return false;

                    if (method.ReturnType != typeof(void)) return false;
                    var args = method.GetParameters();
                    if (args.Length != 2) return false;
                    if (args[0].ParameterType != typeof(ServiceBinderBase)) return false;
                    if (!args[1].ParameterType.IsAssignableFrom(typeof(T))) return false;
                    return true;

                }, binderAttrib.BindMethodName).OfType<MethodInfo>().SingleOrDefault();
            if (method is null) throw new InvalidOperationException("No suitable " + binderAttrib.BindType.Name + "." + binderAttrib.BindMethodName + " method found");

            server ??= Activator.CreateInstance<T>();
            method.Invoke(null, new object[] { binder, server });
        }
    }
}
