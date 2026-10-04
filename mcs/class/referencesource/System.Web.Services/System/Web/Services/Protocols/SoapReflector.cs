//------------------------------------------------------------------------------
// <copyright file="SoapReflector.cs" company="Microsoft">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>                                                                
//------------------------------------------------------------------------------

namespace System.Web.Services.Protocols {
    using System;
    using System.Reflection;
    using System.Xml.Serialization;
    using System.Collections;
    using System.Web.Services.Configuration;
    using System.Web.Services.Description;
    using System.Globalization;
    using System.Xml;
    using System.Threading;

    internal class SoapReflectedHeader {
        internal Type headerType;
        internal MemberInfo memberInfo;
        internal SoapHeaderDirection direction;
        internal bool repeats;
        internal bool custom;
    }

    internal class SoapReflectedExtension : IComparable {
        Type type;
        SoapExtensionAttribute attribute;
        int priority;

        internal SoapReflectedExtension(Type type, SoapExtensionAttribute attribute) : this(type, attribute, attribute.Priority) { }

        internal SoapReflectedExtension(Type type, SoapExtensionAttribute attribute, int priority) {
            if (priority < 0) throw new ArgumentException(Res.GetString(Res.WebConfigInvalidExtensionPriority, priority), "priority");
            this.type = type;
            this.attribute = attribute;
            this.priority = priority;
        }

        internal SoapExtension CreateInstance(object initializer) {
            SoapExtension extension = (SoapExtension)Activator.CreateInstance(type);
            extension.Initialize(initializer);
            return extension;
        }

        internal object GetInitializer(LogicalMethodInfo methodInfo) {
            SoapExtension extension = (SoapExtension)Activator.CreateInstance(type);
            return extension.GetInitializer(methodInfo, attribute);
        }

        internal object GetInitializer(Type serviceType) {
            SoapExtension extension = (SoapExtension) Activator.CreateInstance(type);
            return extension.GetInitializer(serviceType);
        }
            
        internal static object[] GetInitializers(LogicalMethodInfo methodInfo, SoapReflectedExtension[] extensions) {
            var initializers = new object[extensions.Length];
            for (var i = 0; i < initializers.Length; i++)
                initializers[i] = extensions[i].GetInitializer(methodInfo);
            return initializers;
        }

        internal static object[] GetInitializers(Type serviceType, SoapReflectedExtension[] extensions) {
            var initializers = new object[extensions.Length];
            for (var i = 0; i < initializers.Length; i++)
                initializers[i] = extensions[i].GetInitializer(serviceType);
            return initializers;
        }

        public int CompareTo(object o) {
            // higher priorities (lower numbers) go at the front of the list
            return priority - ((SoapReflectedExtension)o).priority;
        }
    }

    internal class SoapReflectedMethod {
        internal LogicalMethodInfo methodInfo;
        internal string action;
        internal string name;
        internal XmlMembersMapping requestMappings;
        internal XmlMembersMapping responseMappings;
        internal XmlMembersMapping inHeaderMappings;
        internal XmlMembersMapping outHeaderMappings;
        internal SoapReflectedHeader[] headers;
        internal SoapReflectedExtension[] extensions;
        internal bool oneWay;
        internal bool rpc;
        internal SoapBindingUse use;
        internal SoapParameterStyle paramStyle;
        internal WebServiceBindingAttribute binding;
        internal XmlQualifiedName requestElementName;
        internal XmlQualifiedName portType;
        internal bool IsClaimsConformance { get { return binding != null && binding.ConformsTo == WsiProfiles.BasicProfile1_1; } }
    }

    // custom attributes are not returned in a "stable" order, so we sort them by name
    internal class SoapHeaderAttributeComparer : IComparer {
        public int Compare(object x, object y) {
            return string.Compare(((SoapHeaderAttribute)x).MemberName, ((SoapHeaderAttribute)y).MemberName, StringComparison.Ordinal);
        }
    }

    internal static class SoapReflector {

        class SoapParameterInfo {
            internal ParameterInfo parameterInfo;
            internal XmlAttributes xmlAttributes;
            internal SoapAttributes soapAttributes;
        }

        class MethodAttribute {
            internal string action;
            internal string binding;
            internal string requestName;
            internal string requestNs;
            internal string responseName;
            internal string responseNs;
        }

        internal static bool ServiceDefaultIsEncoded(Type type) {
            return ServiceDefaultIsEncoded(GetSoapServiceAttribute(type));
        }

        internal static bool ServiceDefaultIsEncoded(object? soapServiceAttribute) {
            if (soapServiceAttribute == null)
                return false;
            if (soapServiceAttribute is SoapDocumentServiceAttribute) {
                return ((SoapDocumentServiceAttribute)soapServiceAttribute).Use == SoapBindingUse.Encoded;
            }
            if (soapServiceAttribute is SoapRpcServiceAttribute) {
                return ((SoapRpcServiceAttribute)soapServiceAttribute).Use == SoapBindingUse.Encoded;
            }
            return false;
        }
        
        internal static string GetEncodedNamespace(string ns, bool serviceDefaultIsEncoded) {
            if (serviceDefaultIsEncoded)
                return ns;
            if (ns.EndsWith("/", StringComparison.Ordinal))
                return ns + "encodedTypes";
            return ns + "/encodedTypes";
        }

        internal static string GetLiteralNamespace(string ns, bool serviceDefaultIsEncoded) {
            if (!serviceDefaultIsEncoded)
                return ns;
            if (ns.EndsWith("/", StringComparison.Ordinal))
                return ns + "literalTypes";
            return ns + "/literalTypes";
        }

        internal static SoapReflectionImporter CreateSoapImporter(string defaultNs, bool serviceDefaultIsEncoded) {
            return new SoapReflectionImporter(GetEncodedNamespace(defaultNs, serviceDefaultIsEncoded));
        }

        internal static XmlReflectionImporter CreateXmlImporter(string defaultNs, bool serviceDefaultIsEncoded) {
            return new XmlReflectionImporter(GetLiteralNamespace(defaultNs, serviceDefaultIsEncoded));
        }
        
        internal static void IncludeTypes(LogicalMethodInfo[] methods, SoapReflectionImporter importer) {
            for (var i = 0; i < methods.Length; i++) {
                var method = methods[i];
                IncludeTypes(method, importer);
            }
        }

        internal static void IncludeTypes(LogicalMethodInfo method, SoapReflectionImporter importer) {
            if (method.Declaration != null) {
                importer.IncludeTypes(method.Declaration.DeclaringType);
                importer.IncludeTypes(method.Declaration);
            }
            importer.IncludeTypes(method.DeclaringType);
            importer.IncludeTypes(method.CustomAttributeProvider);
        }

        internal static object GetSoapMethodAttribute(LogicalMethodInfo methodInfo) {
            var rpcMethodAttributes = methodInfo.GetCustomAttributes(typeof(SoapRpcMethodAttribute));
            var docMethodAttributes = methodInfo.GetCustomAttributes(typeof(SoapDocumentMethodAttribute));
            if (rpcMethodAttributes.Length > 0) {
                if (docMethodAttributes.Length > 0) throw new ArgumentException(Res.GetString(Res.WebBothMethodAttrs), "methodInfo");
                return rpcMethodAttributes[0];
            }
            else if (docMethodAttributes.Length > 0)
                return docMethodAttributes[0];
            else
                return null;
        }

        internal static object GetSoapServiceAttribute(Type type) {
            var rpcServiceAttributes = type.GetCustomAttributes(typeof(SoapRpcServiceAttribute), false);
            var docServiceAttributes = type.GetCustomAttributes(typeof(SoapDocumentServiceAttribute), false);
            if (rpcServiceAttributes.Length > 0) {
                if (docServiceAttributes.Length > 0) throw new ArgumentException(Res.GetString(Res.WebBothServiceAttrs), "methodInfo");
                return rpcServiceAttributes[0];
            }
            else if (docServiceAttributes.Length > 0)
                return docServiceAttributes[0];
            else
                return null;
        }

        internal static SoapServiceRoutingStyle GetSoapServiceRoutingStyle(object soapServiceAttribute) {
            if (soapServiceAttribute is SoapRpcServiceAttribute) 
                return ((SoapRpcServiceAttribute)soapServiceAttribute).RoutingStyle;
            else if (soapServiceAttribute is SoapDocumentServiceAttribute) 
                return ((SoapDocumentServiceAttribute)soapServiceAttribute).RoutingStyle;
            else 
                return SoapServiceRoutingStyle.SoapAction;
        }

        internal static string GetSoapMethodBinding(LogicalMethodInfo method) {
            string binding;
            var attrs = method.GetCustomAttributes(typeof(SoapDocumentMethodAttribute));
            if (attrs.Length == 0) {
                attrs = method.GetCustomAttributes(typeof(SoapRpcMethodAttribute));
                if (attrs.Length == 0) 
                    binding = string.Empty;
                else
                    binding = ((SoapRpcMethodAttribute)attrs[0]).Binding;
            }
            else
                binding = ((SoapDocumentMethodAttribute)attrs[0]).Binding;

            if (method.Binding != null) {
                if (binding.Length > 0 && binding != method.Binding.Name) {
                    throw new InvalidOperationException(Res.GetString(Res.WebInvalidBindingName, binding, method.Binding.Name));
                }
                return method.Binding.Name;
            }
            return binding;
        }

        internal static SoapReflectedMethod ReflectMethod(LogicalMethodInfo methodInfo, bool client, XmlReflectionImporter xmlImporter, SoapReflectionImporter soapImporter, string defaultNs) {
            try {
                var methodId = methodInfo.GetKey();
                var soapMethod = new SoapReflectedMethod();
                var methodAttribute = new MethodAttribute();

                var serviceAttr = GetSoapServiceAttribute(methodInfo.DeclaringType);
                var serviceDefaultIsEncoded = ServiceDefaultIsEncoded(serviceAttr);
                var methodAttr = GetSoapMethodAttribute(methodInfo);
                if (methodAttr == null) {
                    if (client) return null; // method attribute required on the client
                    if (serviceAttr is SoapRpcServiceAttribute) {
                        var method = new SoapRpcMethodAttribute();
                        method.Use = ((SoapRpcServiceAttribute)serviceAttr).Use;
                        methodAttr = method;
                    }
                    else if (serviceAttr is SoapDocumentServiceAttribute) {
                        var method = new SoapDocumentMethodAttribute();
                        method.Use = ((SoapDocumentServiceAttribute)serviceAttr).Use;
                        methodAttr = method;
                    }
                    else {
                        methodAttr = new SoapDocumentMethodAttribute();
                    }
                }

                if (methodAttr is SoapRpcMethodAttribute) {
                    var attr = (SoapRpcMethodAttribute)methodAttr;

                    soapMethod.rpc = true;
                    soapMethod.use = attr.Use;
                    soapMethod.oneWay = attr.OneWay;
                    methodAttribute.action = attr.Action;
                    methodAttribute.binding = attr.Binding;
                    methodAttribute.requestName = attr.RequestElementName;
                    methodAttribute.requestNs = attr.RequestNamespace;
                    methodAttribute.responseName = attr.ResponseElementName;
                    methodAttribute.responseNs = attr.ResponseNamespace;
                }
                else {
                    var attr = (SoapDocumentMethodAttribute)methodAttr;
                    
                    soapMethod.rpc = false;
                    soapMethod.use = attr.Use;
                    soapMethod.paramStyle = attr.ParameterStyle;
                    soapMethod.oneWay = attr.OneWay;
                    methodAttribute.action = attr.Action;
                    methodAttribute.binding = attr.Binding;
                    methodAttribute.requestName = attr.RequestElementName;
                    methodAttribute.requestNs = attr.RequestNamespace;
                    methodAttribute.responseName = attr.ResponseElementName;
                    methodAttribute.responseNs = attr.ResponseNamespace;

                    if (soapMethod.use == SoapBindingUse.Default) {
                        if (serviceAttr is SoapDocumentServiceAttribute)
                            soapMethod.use = ((SoapDocumentServiceAttribute)serviceAttr).Use;
                        if (soapMethod.use == SoapBindingUse.Default)
                            soapMethod.use = SoapBindingUse.Literal;
                    }
                    if (soapMethod.paramStyle == SoapParameterStyle.Default) {
                        if (serviceAttr is SoapDocumentServiceAttribute)
                            soapMethod.paramStyle = ((SoapDocumentServiceAttribute)serviceAttr).ParameterStyle;
                        if (soapMethod.paramStyle == SoapParameterStyle.Default)
                            soapMethod.paramStyle = SoapParameterStyle.Wrapped;
                    }
                }

                if (methodAttribute.binding.Length > 0) {
                    if (client) throw new InvalidOperationException(Res.GetString(Res.WebInvalidBindingPlacement, methodAttr.GetType().Name));
                    soapMethod.binding = WebServiceBindingReflector.GetAttribute(methodInfo, methodAttribute.binding);
                }

                var webMethodAttribute = methodInfo.MethodAttribute;

                // 
                soapMethod.name = webMethodAttribute.MessageName;
                if (soapMethod.name.Length == 0) soapMethod.name = methodInfo.Name;

                string requestElementName;
                if (soapMethod.rpc) {
                    // in the case when we interop with non .net we might need to chnage the method name.
                    requestElementName = methodAttribute.requestName.Length == 0 || !client ? methodInfo.Name : methodAttribute.requestName;
                }
                else {
                    requestElementName = methodAttribute.requestName.Length == 0 ? soapMethod.name : methodAttribute.requestName;
                }
                var requestNamespace = methodAttribute.requestNs;

                if (requestNamespace == null) {
                    if (soapMethod.binding != null && soapMethod.binding.Namespace != null && soapMethod.binding.Namespace.Length != 0)
                        requestNamespace = soapMethod.binding.Namespace;
                    else
                        requestNamespace = defaultNs;
                }

                string responseElementName;
                if (soapMethod.rpc && soapMethod.use != SoapBindingUse.Encoded)
                {
                    // NOTE: this rule should apply equally to rpc/lit and rpc/enc, but to reduce risk, i'm only applying it to rpc/lit
                    responseElementName = methodInfo.Name + "Response";
                }
                else
                {
                    responseElementName = methodAttribute.responseName.Length == 0 ? soapMethod.name + "Response" : methodAttribute.responseName;
                }
                var responseNamespace = methodAttribute.responseNs;

                if (responseNamespace == null) {
                    if (soapMethod.binding != null && soapMethod.binding.Namespace != null && soapMethod.binding.Namespace.Length != 0)
                        responseNamespace = soapMethod.binding.Namespace;
                    else
                        responseNamespace = defaultNs;
                }

                var inParameters = ReflectParameters(methodInfo.InParameters, requestNamespace);
                var outParameters = ReflectParameters(methodInfo.OutParameters, responseNamespace);

                soapMethod.action = methodAttribute.action;
                if (soapMethod.action == null)
                    soapMethod.action = GetDefaultAction(defaultNs, methodInfo);
                soapMethod.methodInfo = methodInfo;

                if (soapMethod.oneWay) {
                    if (outParameters.Length > 0) throw new ArgumentException(Res.GetString(Res.WebOneWayOutParameters), "methodInfo");
                    if (methodInfo.ReturnType != typeof(void)) throw new ArgumentException(Res.GetString(Res.WebOneWayReturnValue), "methodInfo");
                }

                var members = new XmlReflectionMember[inParameters.Length];
                for (var i = 0; i < members.Length; i++) {
                    var soapParamInfo = inParameters[i];
                    var member = new XmlReflectionMember();
                    member.MemberName = soapParamInfo.parameterInfo.Name;
                    member.MemberType = soapParamInfo.parameterInfo.ParameterType;
                    if (member.MemberType.IsByRef)
                        member.MemberType = member.MemberType.GetElementType();
                    member.XmlAttributes = soapParamInfo.xmlAttributes;
                    member.SoapAttributes = soapParamInfo.soapAttributes;
                    members[i] = member;
                }
                soapMethod.requestMappings = ImportMembersMapping(xmlImporter, soapImporter, serviceDefaultIsEncoded, soapMethod.rpc, soapMethod.use, soapMethod.paramStyle, requestElementName, requestNamespace, methodAttribute.requestNs == null, members, true, false, methodId, client);

                if (GetSoapServiceRoutingStyle(serviceAttr) == SoapServiceRoutingStyle.RequestElement &&
                    soapMethod.paramStyle == SoapParameterStyle.Bare &&
                    soapMethod.requestMappings.Count != 1)
                    throw new ArgumentException(Res.GetString(Res.WhenUsingAMessageStyleOfParametersAsDocument0), "methodInfo");

                var elementName = "";
                var elementNamespace = "";
                if (soapMethod.paramStyle == SoapParameterStyle.Bare) {
                    if (soapMethod.requestMappings.Count == 1) {
                        elementName = soapMethod.requestMappings[0].XsdElementName;
                        elementNamespace = soapMethod.requestMappings[0].Namespace;
                    }
                    // else: can't route on request element -- we match on an empty qname, 
                    //       normal rules apply for duplicates
                }
                else {
                    elementName = soapMethod.requestMappings.XsdElementName;
                    elementNamespace = soapMethod.requestMappings.Namespace;
                }
                soapMethod.requestElementName = new XmlQualifiedName(elementName, elementNamespace);
                
                if (!soapMethod.oneWay) {
                    var numOutParams = outParameters.Length;
                    var count = 0;
                    CodeIdentifiers identifiers = null;
                    if (methodInfo.ReturnType != typeof(void)) {
                        numOutParams++;
                        count = 1;
                        identifiers = new CodeIdentifiers();
                    }
                    members = new XmlReflectionMember[numOutParams];

                    for (var i = 0; i < outParameters.Length; i++) {
                        var soapParamInfo = outParameters[i];
                        var member = new XmlReflectionMember();
                        member.MemberName = soapParamInfo.parameterInfo.Name;
                        member.MemberType = soapParamInfo.parameterInfo.ParameterType;
                        if (member.MemberType.IsByRef)
                            member.MemberType = member.MemberType.GetElementType();
                        member.XmlAttributes = soapParamInfo.xmlAttributes;
                        member.SoapAttributes = soapParamInfo.soapAttributes;
                        members[count++] = member;
                        if (identifiers != null)
                            identifiers.Add(member.MemberName, null);
                    }
                    if (methodInfo.ReturnType != typeof(void)) {
                        var member = new XmlReflectionMember();
                        member.MemberName = identifiers.MakeUnique(soapMethod.name + "Result");
                        member.MemberType = methodInfo.ReturnType;
                        member.IsReturnValue = true;

                        member.XmlAttributes = new XmlAttributes(methodInfo.ReturnTypeCustomAttributeProvider);
                        member.XmlAttributes.XmlRoot = null; // Ignore XmlRoot attribute used by get/post
                        member.SoapAttributes = new SoapAttributes(methodInfo.ReturnTypeCustomAttributeProvider);

                        members[0] = member;
                    }
                    soapMethod.responseMappings = ImportMembersMapping(xmlImporter, soapImporter, serviceDefaultIsEncoded, soapMethod.rpc, soapMethod.use, soapMethod.paramStyle, responseElementName, responseNamespace, methodAttribute.responseNs == null, members, false, false, methodId + ":Response", !client);

                }

                var extensionAttributes = (SoapExtensionAttribute[])methodInfo.GetCustomAttributes(typeof(SoapExtensionAttribute));
                soapMethod.extensions = new SoapReflectedExtension[extensionAttributes.Length];
                for (var i = 0; i < extensionAttributes.Length; i++)
                    soapMethod.extensions[i] = new SoapReflectedExtension(extensionAttributes[i].ExtensionType, extensionAttributes[i]);
                Array.Sort(soapMethod.extensions);

                var headerAttributes = (SoapHeaderAttribute[])methodInfo.GetCustomAttributes(typeof(SoapHeaderAttribute));
                Array.Sort(headerAttributes, new SoapHeaderAttributeComparer());
                var headerTypes = new Hashtable();
                soapMethod.headers = new SoapReflectedHeader[headerAttributes.Length];
                var front = 0;
                var back = soapMethod.headers.Length;
                var inHeaders = new ArrayList();
                var outHeaders = new ArrayList();
                for (var i = 0; i < soapMethod.headers.Length; i++) {
                    var headerAttribute = headerAttributes[i];
                    var soapHeader = new SoapReflectedHeader();
                    var declaringType = methodInfo.DeclaringType;
                    if ((soapHeader.memberInfo = declaringType.GetField(headerAttribute.MemberName)) != null) {
                        soapHeader.headerType = ((FieldInfo)soapHeader.memberInfo).FieldType;
                    }
                    else if ((soapHeader.memberInfo = declaringType.GetProperty(headerAttribute.MemberName)) != null) {
                        soapHeader.headerType = ((PropertyInfo)soapHeader.memberInfo).PropertyType;
                    }
                    else {
                        throw HeaderException(headerAttribute.MemberName, methodInfo.DeclaringType, Res.WebHeaderMissing);
                    }
                    if (soapHeader.headerType.IsArray) {
                        soapHeader.headerType = soapHeader.headerType.GetElementType();
                        soapHeader.repeats = true;
                        if (soapHeader.headerType != typeof(SoapUnknownHeader) && soapHeader.headerType != typeof(SoapHeader))
                            throw HeaderException(headerAttribute.MemberName, methodInfo.DeclaringType, Res.WebHeaderType);
                    }
                    if (MemberHelper.IsStatic(soapHeader.memberInfo)) throw HeaderException(headerAttribute.MemberName, methodInfo.DeclaringType, Res.WebHeaderStatic);
                    if (!MemberHelper.CanRead(soapHeader.memberInfo)) throw HeaderException(headerAttribute.MemberName, methodInfo.DeclaringType, Res.WebHeaderRead);
                    if (!MemberHelper.CanWrite(soapHeader.memberInfo)) throw HeaderException(headerAttribute.MemberName, methodInfo.DeclaringType, Res.WebHeaderWrite);
                    if (!typeof(SoapHeader).IsAssignableFrom(soapHeader.headerType)) throw HeaderException(headerAttribute.MemberName, methodInfo.DeclaringType, Res.WebHeaderType);
                    
                    var direction = headerAttribute.Direction;
                    if (soapMethod.oneWay && (direction & (SoapHeaderDirection.Out | SoapHeaderDirection.Fault)) != 0) throw HeaderException(headerAttribute.MemberName, methodInfo.DeclaringType, Res.WebHeaderOneWayOut);
                    if (headerTypes.Contains(soapHeader.headerType)) {
                        var prevDirection = (SoapHeaderDirection) headerTypes[soapHeader.headerType];
                        if ((prevDirection & direction) != 0)
                            throw HeaderException(headerAttribute.MemberName, methodInfo.DeclaringType, Res.WebMultiplyDeclaredHeaderTypes);
                        headerTypes[soapHeader.headerType] = direction | prevDirection;
                    }
                    else
                        headerTypes[soapHeader.headerType] = direction;
                    
                    if (soapHeader.headerType != typeof(SoapHeader) && soapHeader.headerType != typeof(SoapUnknownHeader)) {
                        var member = new XmlReflectionMember();
                        member.MemberName = soapHeader.headerType.Name;
                        member.MemberType = soapHeader.headerType;

                        var a = new XmlAttributes(soapHeader.headerType);
                        if (a.XmlRoot != null) {
                            member.XmlAttributes = new XmlAttributes();
                            var attr = new XmlElementAttribute();
                            attr.ElementName = a.XmlRoot.ElementName;
                            attr.Namespace = a.XmlRoot.Namespace;
                            member.XmlAttributes.XmlElements.Add(attr);
                        }
                        member.OverrideIsNullable = true;
                        
                        if ((direction & SoapHeaderDirection.In) != 0)
                            inHeaders.Add(member);
                        if ((direction & (SoapHeaderDirection.Out | SoapHeaderDirection.Fault)) != 0)
                            outHeaders.Add(member);

                        soapHeader.custom = true;
                    }
                    soapHeader.direction = direction;
                    // Put generic header mappings at the end of the list so they are found last during header processing
                    if (!soapHeader.custom) {
                        soapMethod.headers[--back] = soapHeader;
                    }
                    else {
                        soapMethod.headers[front++] = soapHeader;
                    }
                }
                soapMethod.inHeaderMappings = ImportMembersMapping(xmlImporter, soapImporter, serviceDefaultIsEncoded, false, soapMethod.use, SoapParameterStyle.Bare, requestElementName + "InHeaders", defaultNs, true, (XmlReflectionMember[]) inHeaders.ToArray(typeof(XmlReflectionMember)), false, true, methodId + ":InHeaders", client);
                if (!soapMethod.oneWay)
                    soapMethod.outHeaderMappings = ImportMembersMapping(xmlImporter, soapImporter, serviceDefaultIsEncoded, false, soapMethod.use, SoapParameterStyle.Bare, responseElementName + "OutHeaders", defaultNs, true, (XmlReflectionMember[]) outHeaders.ToArray(typeof(XmlReflectionMember)), false, true, methodId + ":OutHeaders", !client);
                
                return soapMethod;
            }
            catch (Exception e) {
                if (e is ThreadAbortException || e is StackOverflowException || e is OutOfMemoryException) {
                    throw;
                }
                throw new InvalidOperationException(Res.GetString(Res.WebReflectionErrorMethod, methodInfo.DeclaringType.Name, methodInfo.Name), e);
            }
        }

        static XmlMembersMapping ImportMembersMapping(XmlReflectionImporter xmlImporter, SoapReflectionImporter soapImporter, bool serviceDefaultIsEncoded, bool rpc, SoapBindingUse use, SoapParameterStyle paramStyle, 
            string elementName, string elementNamespace, bool nsIsDefault, XmlReflectionMember[] members, bool validate, bool openModel, string key, bool writeAccess) {
            XmlMembersMapping mapping = null;
            if (use == SoapBindingUse.Encoded) {
                var ns = (!rpc && paramStyle != SoapParameterStyle.Bare && nsIsDefault) ? GetEncodedNamespace(elementNamespace, serviceDefaultIsEncoded) : elementNamespace;
                mapping = soapImporter.ImportMembersMapping(elementName, ns, members, rpc || paramStyle != SoapParameterStyle.Bare, rpc, validate, writeAccess ? XmlMappingAccess.Write : XmlMappingAccess.Read);
            }
            else {
                var ns = nsIsDefault ? GetLiteralNamespace(elementNamespace, serviceDefaultIsEncoded) : elementNamespace;
                mapping = xmlImporter.ImportMembersMapping(elementName, ns, members, paramStyle != SoapParameterStyle.Bare, rpc, openModel, writeAccess ? XmlMappingAccess.Write : XmlMappingAccess.Read);
            }
            if (mapping != null) {
                mapping.SetKey(key);
            }
            return mapping;
        }

        static Exception HeaderException(string memberName, Type declaringType, string description) {
            return new Exception(Res.GetString(description, declaringType.Name, memberName));
        }

        static SoapParameterInfo[] ReflectParameters(ParameterInfo[] paramInfos, string ns) {
            var soapParamInfos = new SoapParameterInfo[paramInfos.Length];
            for (var i = 0; i < paramInfos.Length; i++) {
                var soapParamInfo = new SoapParameterInfo();
                
                var paramInfo = paramInfos[i];

                if (paramInfo.ParameterType.IsArray && paramInfo.ParameterType.GetArrayRank() > 1)
                    throw new InvalidOperationException(Res.GetString(Res.WebMultiDimArray));

                soapParamInfo.xmlAttributes = new XmlAttributes(paramInfo);
                soapParamInfo.soapAttributes = new SoapAttributes(paramInfo);
                soapParamInfo.parameterInfo = paramInfo;
                soapParamInfos[i] = soapParamInfo;
            }
            return soapParamInfos;
        } 

        static string GetDefaultAction(string defaultNs, LogicalMethodInfo methodInfo) {
            var methodAttribute = methodInfo.MethodAttribute;
            var messageName = methodAttribute.MessageName;
            if (messageName.Length == 0) messageName = methodInfo.Name;
            if (defaultNs.EndsWith("/", StringComparison.Ordinal))
                return defaultNs + messageName;
            return defaultNs + "/" + messageName;
        }
    }
}
