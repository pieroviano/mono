//------------------------------------------------------------------------------
// <copyright file="DiscoveryClientProtocol.cs" company="Microsoft">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

namespace System.Web.Services.Discovery {

    using System.Xml.Serialization;
    using System.IO;
    using System;
    using System.Web.Services;
    using System.Web.Services.Protocols;
    using System.Net;
    using System.Collections;
    using System.Diagnostics;
    using System.Web.Services.Configuration;
    using System.Text;
    using System.Security.Permissions;
    using System.Globalization;
    using System.Threading;
    using System.Runtime.InteropServices;
    using System.Web.Services.Diagnostics;

    /// <include file='doc\DiscoveryClientProtocol.uex' path='docs/doc[@for="DiscoveryClientProtocol"]/*' />
    /// <devdoc>
    ///    <para>[To be supplied.]</para>
    /// </devdoc>
    public class DiscoveryClientProtocol : HttpWebClientProtocol {
        private DiscoveryClientReferenceCollection references = new DiscoveryClientReferenceCollection();
        private DiscoveryClientDocumentCollection documents = new DiscoveryClientDocumentCollection();
        private Hashtable inlinedSchemas = new Hashtable();
        private ArrayList additionalInformation = new ArrayList();
        private DiscoveryExceptionDictionary errors = new DiscoveryExceptionDictionary();

        /// <include file='doc\DiscoveryClientProtocol.uex' path='docs/doc[@for="DiscoveryClientProtocol.DiscoveryClientProtocol"]/*' />
        /// <devdoc>
        ///    <para>[To be supplied.]</para>
        /// </devdoc>
        public DiscoveryClientProtocol()
            : base() {
        }

        internal DiscoveryClientProtocol(HttpWebClientProtocol protocol) : base(protocol) {
        }

        /// <include file='doc\DiscoveryClientProtocol.uex' path='docs/doc[@for="DiscoveryClientProtocol.AdditionalInformation"]/*' />
        /// <devdoc>
        ///    <para>[To be supplied.]</para>
        /// </devdoc>
        public IList AdditionalInformation {
            get {
                return additionalInformation;
            }
        }

        /// <include file='doc\DiscoveryClientProtocol.uex' path='docs/doc[@for="DiscoveryClientProtocol.Documents"]/*' />
        /// <devdoc>
        ///    <para>[To be supplied.]</para>
        /// </devdoc>
        public DiscoveryClientDocumentCollection Documents {
            get {
                return documents;
            }
        }

        /// <include file='doc\DiscoveryClientProtocol.uex' path='docs/doc[@for="DiscoveryClientProtocol.Errors"]/*' />
        /// <devdoc>
        ///    <para>[To be supplied.]</para>
        /// </devdoc>
        public DiscoveryExceptionDictionary Errors {
            get {
                return errors;
            }
        }

        /// <include file='doc\DiscoveryClientProtocol.uex' path='docs/doc[@for="DiscoveryClientProtocol.References"]/*' />
        /// <devdoc>
        ///    <para>[To be supplied.]</para>
        /// </devdoc>
        public DiscoveryClientReferenceCollection References {
            get {
                return references;
            }
        }

        /// <include file='doc\DiscoveryClientProtocol.uex' path='docs/doc[@for="DiscoveryClientProtocol.References"]/*' />
        /// <devdoc>
        ///    <para>[To be supplied.]</para>
        /// </devdoc>
        internal Hashtable InlinedSchemas
        {
            get 
            {
                return inlinedSchemas;
            }
        }
        /// <include file='doc\DiscoveryClientProtocol.uex' path='docs/doc[@for="DiscoveryClientProtocol.Discover"]/*' />
        /// <devdoc>
        ///    <para>[To be supplied.]</para>
        /// </devdoc>
        [PermissionSet(SecurityAction.LinkDemand, Name = "FullTrust")]
        public DiscoveryDocument Discover(string url) {
            DiscoveryDocument doc = Documents[url] as DiscoveryDocument;
            if (doc != null)
                return doc;

            var docRef = new DiscoveryDocumentReference(url);
            docRef.ClientProtocol = this;
            References[url] = docRef;

            Errors.Clear();
            // this will auto-resolve and place the document in the Documents hashtable.
            return docRef.Document;
        }

        /// <include file='doc\DiscoveryClientProtocol.uex' path='docs/doc[@for="DiscoveryClientProtocol.DiscoverAny"]/*' />
        /// <devdoc>
        ///    <para>[To be supplied.]</para>
        /// </devdoc>
        [PermissionSet(SecurityAction.LinkDemand, Name = "FullTrust")]
        public DiscoveryDocument DiscoverAny(string url) {
            var refTypes = WebServicesSection.Current.DiscoveryReferenceTypes;
            DiscoveryReference discoRef = null;
            string contentType = null;
            var stream = Download(ref url, ref contentType);

            Errors.Clear();
            var allErrorsAreHtmlContentType = true;
            Exception errorInValidDocument = null;
            var specialErrorMessages = new ArrayList();
            foreach (var type in refTypes) {
                if (!typeof(DiscoveryReference).IsAssignableFrom(type))
                    continue;
                discoRef = (DiscoveryReference) Activator.CreateInstance(type);
                discoRef.Url = url;
                discoRef.ClientProtocol = this;
                stream.Position = 0;
                var e = discoRef.AttemptResolve(contentType, stream);
                if (e == null)
                    break;

                Errors[type.FullName] = e;
                discoRef = null;

                InvalidContentTypeException e2 = e as InvalidContentTypeException;
                if (e2 == null || !ContentType.MatchesBase(e2.ContentType, "text/html"))
                    allErrorsAreHtmlContentType = false;

                InvalidDocumentContentsException e3 = e as InvalidDocumentContentsException;
                if (e3 != null) {
                    errorInValidDocument = e;
                    break;
                }

                if (e.InnerException != null && e.InnerException.InnerException == null)
                    specialErrorMessages.Add(e.InnerException.Message);
            }

            if (discoRef == null) {
                if (errorInValidDocument != null) {
                    var errorMessage = new StringBuilder(Res.GetString(Res.TheDocumentWasUnderstoodButContainsErrors));
                    while (errorInValidDocument != null) {
                        errorMessage.Append("\n  - ").Append(errorInValidDocument.Message);
                        errorInValidDocument = errorInValidDocument.InnerException;
                    }
                    throw new InvalidOperationException(errorMessage.ToString());
                }
                else if (allErrorsAreHtmlContentType) {
                    throw new InvalidOperationException(Res.GetString(Res.TheHTMLDocumentDoesNotContainDiscoveryInformation));
                }
                else {
                    var same = specialErrorMessages.Count == Errors.Count && Errors.Count > 0;
                    for (var i = 1; same && i < specialErrorMessages.Count; i++) {
                        if ((string) specialErrorMessages[i - 1] != (string) specialErrorMessages[i])
                            same = false;
                    }
                    if (same)
                        throw new InvalidOperationException(Res.GetString(Res.TheDocumentWasNotRecognizedAsAKnownDocumentType, specialErrorMessages[0]));
                    else {
                        Exception e;
                        var errorMessage = new StringBuilder(Res.GetString(Res.WebMissingResource, url));
                        foreach (DictionaryEntry entry in Errors) {
                            e = (Exception)(entry.Value);
                            var refType = (string)(entry.Key);
                            if (0 == string.Compare(refType, typeof(ContractReference).FullName, StringComparison.Ordinal)) {
                                refType = Res.GetString(Res.WebContractReferenceName);
                            }
                            else if (0 == string.Compare(refType, typeof(SchemaReference).FullName, StringComparison.Ordinal)) {
                                refType = Res.GetString(Res.WebShemaReferenceName);
                            }
                            else if (0 == string.Compare(refType, typeof(DiscoveryDocumentReference).FullName, StringComparison.Ordinal)) {
                                refType = Res.GetString(Res.WebDiscoveryDocumentReferenceName);
                            }
                            errorMessage.Append("\n- ").Append(Res.GetString(Res.WebDiscoRefReport,
                                                               refType,
                                                               e.Message));
                            while (e.InnerException != null) {
                                errorMessage.Append("\n  - ").Append(e.InnerException.Message);
                                e = e.InnerException;
                            }
                        }
                        throw new InvalidOperationException(errorMessage.ToString());
                    }
                }
            }

            if (discoRef is DiscoveryDocumentReference)
                return ((DiscoveryDocumentReference) discoRef).Document;

            References[discoRef.Url] = discoRef;
            var doc = new DiscoveryDocument();
            doc.References.Add(discoRef);
            return doc;
        }

        /// <include file='doc\DiscoveryClientProtocol.uex' path='docs/doc[@for="DiscoveryClientProtocol.Download"]/*' />
        /// <devdoc>
        ///    <para>[To be supplied.]</para>
        /// </devdoc>
        [PermissionSet(SecurityAction.LinkDemand, Name = "FullTrust")]
        public Stream Download(ref string url) {
            string contentType = null;
            return Download(ref url, ref contentType);
        }

        /// <include file='doc\DiscoveryClientProtocol.uex' path='docs/doc[@for="DiscoveryClientProtocol.Download1"]/*' />
        /// <devdoc>
        ///    <para>[To be supplied.]</para>
        /// </devdoc>
        [PermissionSet(SecurityAction.LinkDemand, Name = "FullTrust")]
        public Stream Download(ref string url, ref string contentType) {
            var request = GetWebRequest(new Uri(url));
            request.Method = "GET";
#if DEBUG // 
            HttpWebRequest httpRequest = request as HttpWebRequest;
            if (httpRequest != null) {
                httpRequest.Timeout = httpRequest.Timeout * 2;
            }
#endif
            WebResponse response = null;
            try {
                response = GetWebResponse(request);
            }
            catch (Exception e) {
                if (e is ThreadAbortException || e is StackOverflowException || e is OutOfMemoryException) {
                    throw;
                }
                throw new WebException(Res.GetString(Res.ThereWasAnErrorDownloading0, url), e);
            }
            HttpWebResponse httpResponse = response as HttpWebResponse;
            if (httpResponse != null) {
                if (httpResponse.StatusCode != HttpStatusCode.OK) {
                    var errorMessage = RequestResponseUtils.CreateResponseExceptionString(httpResponse);
                    throw new WebException(Res.GetString(Res.ThereWasAnErrorDownloading0, url), new WebException(errorMessage, null, WebExceptionStatus.ProtocolError, response));
                }
            }
            var responseStream = response.GetResponseStream();
            try {
                // Uri.ToString() returns the unescaped version
                url = response.ResponseUri.ToString();
                contentType = response.ContentType;

                if (response.ResponseUri.Scheme == Uri.UriSchemeFtp ||
                    response.ResponseUri.Scheme == Uri.UriSchemeFile) {
                    var dotIndex = response.ResponseUri.AbsolutePath.LastIndexOf('.');
                    if (dotIndex != -1) {
                        switch (response.ResponseUri.AbsolutePath.Substring(dotIndex + 1).ToLower(CultureInfo.InvariantCulture)) {
                            case "xml":
                            case "wsdl":
                            case "xsd":
                            case "disco":
                                contentType = ContentType.TextXml;
                                break;
                            default:
                                break;
                        }
                    }
                }

                // need to return a buffered stream (one that supports CanSeek)
                return RequestResponseUtils.StreamToMemoryStream(responseStream);
            }
            finally {
                responseStream.Close();
            }
        }

        /// <include file='doc\DiscoveryClientProtocol.uex' path='docs/doc[@for="DiscoveryClientProtocol.LoadExternals"]/*' />
        /// <devdoc>
        ///    <para>[To be supplied.]</para>
        /// <internalonly/>
        /// </devdoc>
        [Obsolete("This method will be removed from a future version. The method call is no longer required for resource discovery", false)]
        [ComVisible(false)]
        public void LoadExternals() { }

        internal void FixupReferences() {
            foreach (DiscoveryReference reference in References.Values) {
                reference.LoadExternals(InlinedSchemas);
            }
            foreach (string url in InlinedSchemas.Keys) {
                Documents.Remove(url);
            }
        }

        private static bool IsFilenameInUse(Hashtable filenames, string path) {
            return filenames[path.ToLower(CultureInfo.InvariantCulture)] != null;
        }

        private static void AddFilename(Hashtable filenames, string path) {
            filenames.Add(path.ToLower(CultureInfo.InvariantCulture), path);
        }

        private static string GetUniqueFilename(Hashtable filenames, string path) {
            if (IsFilenameInUse(filenames, path)) {
                var extension = Path.GetExtension(path);
                var allElse = path.Substring(0, path.Length - extension.Length);
                var append = 0;
                do {
                    path = allElse + append.ToString(CultureInfo.InvariantCulture) + extension;
                    append++;
                } while (IsFilenameInUse(filenames, path));
            }

            AddFilename(filenames, path);
            return path;
        }

        /// <include file='doc\DiscoveryClientProtocol.uex' path='docs/doc[@for="DiscoveryClientProtocol.ReadAll"]/*' />
        /// <devdoc>
        ///    <para>[To be supplied.]</para>
        /// </devdoc>
        [PermissionSet(SecurityAction.LinkDemand, Name = "FullTrust")]
        public DiscoveryClientResultCollection ReadAll(string topLevelFilename) {
            var ser = new XmlSerializer(typeof(DiscoveryClientResultsFile));
            Stream file = File.OpenRead(topLevelFilename);
            string topLevelPath = Path.GetDirectoryName(topLevelFilename);
            DiscoveryClientResultsFile results = null;
            try {
                results = (DiscoveryClientResultsFile) ser.Deserialize(file);
                for (var i = 0; i < results.Results.Count; i++) {
                    if (results.Results[i] == null)
                        throw new InvalidOperationException(Res.GetString(Res.WebNullRef));
                    var typeName = results.Results[i].ReferenceTypeName;
                    if (typeName == null || typeName.Length == 0)
                        throw new InvalidOperationException(Res.GetString(Res.WebRefInvalidAttribute, "referenceType"));
                    DiscoveryReference reference = (DiscoveryReference) Activator.CreateInstance(Type.GetType(typeName));
                    reference.ClientProtocol = this;

                    var url = results.Results[i].Url;
                    if (url == null || url.Length == 0)
                        throw new InvalidOperationException(Res.GetString(Res.WebRefInvalidAttribute2, reference.GetType().FullName, "url"));
                    reference.Url = url;
                    var fileName = results.Results[i].Filename;
                    if (fileName == null || fileName.Length == 0)
                        throw new InvalidOperationException(Res.GetString(Res.WebRefInvalidAttribute2, reference.GetType().FullName, "filename"));

                    Stream docFile = File.OpenRead(Path.Combine(topLevelPath, results.Results[i].Filename));
                    try {
                        Documents[reference.Url] = reference.ReadDocument(docFile);
                        Debug.Assert(Documents[reference.Url] != null, "Couldn't deserialize file " + results.Results[i].Filename);
                    }
                    finally {
                        docFile.Close();
                    }
                    References[reference.Url] = reference;
                }
                ResolveAll();
            }
            finally {
                file.Close();
            }
            return results.Results;
        }

        /// <include file='doc\DiscoveryClientProtocol.uex' path='docs/doc[@for="DiscoveryClientProtocol.ResolveAll"]/*' />
        /// <devdoc>
        ///    <para>[To be supplied.]</para>
        /// </devdoc>
        [PermissionSet(SecurityAction.LinkDemand, Name = "FullTrust")]
        public void ResolveAll() {
            // Resolve until we reach a 'steady state' (no more references added)
            Errors.Clear();
            var resolvedCount = InlinedSchemas.Keys.Count;
            while (resolvedCount != References.Count) {
                resolvedCount = References.Count;
                var refs = new DiscoveryReference[References.Count];
                References.Values.CopyTo(refs, 0);
                for (var i = 0; i < refs.Length; i++) {
                    var discoRef = refs[i];
                    if (discoRef is DiscoveryDocumentReference) {
                        try {
                            // Resolve discovery document references deeply
                            ((DiscoveryDocumentReference)discoRef).ResolveAll(true);
                        }
                        catch (Exception e) {
                            if (e is ThreadAbortException || e is StackOverflowException || e is OutOfMemoryException) {
                                throw;
                            }
                            // don't let the exception out - keep going. Just add it to the list of errors.
                            Errors[discoRef.Url] = e;
                            if (Tracing.On) Tracing.ExceptionCatch(TraceEventType.Warning, this, "ResolveAll", e);
                        }
                    }
                    else {
                        try {
                            discoRef.Resolve();
                        }
                        catch (Exception e) {
                            if (e is ThreadAbortException || e is StackOverflowException || e is OutOfMemoryException) {
                                throw;
                            }
                            // don't let the exception out - keep going. Just add it to the list of errors.
                            Errors[discoRef.Url] = e;
                            if (Tracing.On) Tracing.ExceptionCatch(TraceEventType.Warning, this, "ResolveAll", e);
                        }
                    }
                }
            }
            FixupReferences();
        }

        /// <include file='doc\DiscoveryClientProtocol.uex' path='docs/doc[@for="DiscoveryClientProtocol.ResolveOneLevel"]/*' />
        /// <devdoc>
        ///    <para>[To be supplied.]</para>
        /// </devdoc>
        [PermissionSet(SecurityAction.LinkDemand, Name = "FullTrust")]
        public void ResolveOneLevel() {
            // download everything we have a reference to, but don't recurse.
            Errors.Clear();
            var refs = new DiscoveryReference[References.Count];
            References.Values.CopyTo(refs, 0);
            for (var i = 0; i < refs.Length; i++) {
                try {
                    refs[i].Resolve();
                }
                catch (Exception e) {
                    if (e is ThreadAbortException || e is StackOverflowException || e is OutOfMemoryException) {
                        throw;
                    }
                    // don't let the exception out - keep going. Just add it to the list of errors.
                    Errors[refs[i].Url] = e;
                    if (Tracing.On) Tracing.ExceptionCatch(TraceEventType.Warning, this, "ResolveOneLevel", e);
                }
            }
        }

        private static string GetRelativePath(string fullPath, string relativeTo) {
            string currentDir = Path.GetDirectoryName(Path.GetFullPath(relativeTo));

            var answer = "";
            while (currentDir.Length > 0) {
                if (currentDir.Length <= fullPath.Length && string.Compare(currentDir, fullPath.Substring(0, currentDir.Length), StringComparison.OrdinalIgnoreCase) == 0) {
                    answer += fullPath.Substring(currentDir.Length);
                    if (answer.StartsWith(""+Path.DirectorySeparatorChar, StringComparison.Ordinal))
                        answer = answer.Substring(1);
                    return answer;
                }
                answer += ".." + Path.DirectorySeparatorChar;
                if (currentDir.Length < 2)
                    break;
                else {
                    var lastSlash = currentDir.LastIndexOf(Path.DirectorySeparatorChar, currentDir.Length - 2);
                    currentDir = currentDir.Substring(0, lastSlash + 1);
                }
            }
            return fullPath;
        }

        /// <include file='doc\DiscoveryClientProtocol.uex' path='docs/doc[@for="DiscoveryClientProtocol.WriteAll"]/*' />
        /// <devdoc>
        ///    <para>[To be supplied.]</para>
        /// </devdoc>
        [PermissionSet(SecurityAction.LinkDemand, Name = "FullTrust")]
        public DiscoveryClientResultCollection WriteAll(string directory, string topLevelFilename) {
            var results = new DiscoveryClientResultsFile();
            var filenames = new Hashtable();
            var topLevelFullPath = Path.Combine(directory, topLevelFilename);

            // write out each of the documents
            var entries = new DictionaryEntry[Documents.Count + InlinedSchemas.Keys.Count];
            var i = 0;
            foreach (DictionaryEntry entry in Documents) {
                entries[i++] = entry;
            }
            foreach (DictionaryEntry entry in InlinedSchemas) {
                entries[i++] = entry;
            }
            foreach (var entry in entries) {
                var url = (string) entry.Key;
                object document = entry.Value;
                if (document == null)
                    continue;
                var reference = References[url];
                var filename = reference == null ? DiscoveryReference.FilenameFromUrl(Url) : reference.DefaultFilename;
                filename = GetUniqueFilename(filenames, Path.GetFullPath(Path.Combine(directory, filename)));
                results.Results.Add(new DiscoveryClientResult(reference == null ? null : reference.GetType(), url, GetRelativePath(filename, topLevelFullPath)));
                Stream file = File.Create(filename);
                try {
                    reference.WriteDocument(document, file);
                }
                finally {
                    file.Close();
                }
            }

            // write out the file that points to all those documents.
            var ser = new XmlSerializer(typeof(DiscoveryClientResultsFile));
            Stream topLevelFile = File.Create(topLevelFullPath);
            try {
                ser.Serialize(new StreamWriter(topLevelFile, new UTF8Encoding(false)), results);
            }
            finally {
                topLevelFile.Close();
            }

            return results.Results;
        }

        // 





        public sealed class DiscoveryClientResultsFile {
            private DiscoveryClientResultCollection results = new DiscoveryClientResultCollection();
            /// <include file='doc\DiscoveryClientProtocol.uex' path='docs/doc[@for="DiscoveryClientProtocol.DiscoveryClientResultsFile.Results"]/*' />
            /// <devdoc>
            ///    <para>[To be supplied.]</para>
            /// </devdoc>
            public DiscoveryClientResultCollection Results {
                get {
                    return results;
                }
            }
        }

    }

    /// <include file='doc\DiscoveryClientProtocol.uex' path='docs/doc[@for="DiscoveryClientResultCollection"]/*' />
    /// <devdoc>
    ///    <para>[To be supplied.]</para>
    /// </devdoc>
    public sealed class DiscoveryClientResultCollection : CollectionBase {

        /// <include file='doc\DiscoveryClientProtocol.uex' path='docs/doc[@for="DiscoveryClientResultCollection.this"]/*' />
        /// <devdoc>
        ///    <para>[To be supplied.]</para>
        /// </devdoc>
        public DiscoveryClientResult this[int i] {
            get {
                return (DiscoveryClientResult) List[i];
            }
            set {
                List[i] = value;
            }
        }

        /// <include file='doc\DiscoveryClientProtocol.uex' path='docs/doc[@for="DiscoveryClientResultCollection.Add"]/*' />
        /// <devdoc>
        ///    <para>[To be supplied.]</para>
        /// </devdoc>
        public int Add(DiscoveryClientResult value) {
            return List.Add(value);
        }

        /// <include file='doc\DiscoveryClientProtocol.uex' path='docs/doc[@for="DiscoveryClientResultCollection.Contains"]/*' />
        /// <devdoc>
        ///    <para>[To be supplied.]</para>
        /// </devdoc>
        public bool Contains(DiscoveryClientResult value) {
            return List.Contains(value);
        }

        /// <include file='doc\DiscoveryClientProtocol.uex' path='docs/doc[@for="DiscoveryClientResultCollection.Remove"]/*' />
        /// <devdoc>
        ///    <para>[To be supplied.]</para>
        /// </devdoc>
        public void Remove(DiscoveryClientResult value) {
            List.Remove(value);
        }

    }

    /// <include file='doc\DiscoveryClientProtocol.uex' path='docs/doc[@for="DiscoveryClientResult"]/*' />
    /// <devdoc>
    ///    <para>[To be supplied.]</para>
    /// </devdoc>
    public sealed class DiscoveryClientResult {
        string referenceTypeName;
        string url;
        string filename;

        /// <include file='doc\DiscoveryClientProtocol.uex' path='docs/doc[@for="DiscoveryClientResult.DiscoveryClientResult"]/*' />
        /// <devdoc>
        ///    <para>[To be supplied.]</para>
        /// </devdoc>
        public DiscoveryClientResult() {
        }

        /// <include file='doc\DiscoveryClientProtocol.uex' path='docs/doc[@for="DiscoveryClientResult.DiscoveryClientResult1"]/*' />
        /// <devdoc>
        ///    <para>[To be supplied.]</para>
        /// </devdoc>
        public DiscoveryClientResult(Type? referenceType, string url, string filename) {
            this.referenceTypeName = referenceType == null ? string.Empty : referenceType.FullName;
            this.url = url;
            this.filename = filename;
        }

        /// <include file='doc\DiscoveryClientProtocol.uex' path='docs/doc[@for="DiscoveryClientResult.ReferenceTypeName"]/*' />
        /// <devdoc>
        ///    <para>[To be supplied.]</para>
        /// </devdoc>
        [XmlAttribute("referenceType")]
        public string ReferenceTypeName {
            get {
                return referenceTypeName;
            }
            set {
                referenceTypeName = value;
            }
        }

        /// <include file='doc\DiscoveryClientProtocol.uex' path='docs/doc[@for="DiscoveryClientResult.Url"]/*' />
        /// <devdoc>
        ///    <para>[To be supplied.]</para>
        /// </devdoc>
        [XmlAttribute("url")]
        public string Url {
            get {
                return url;
            }
            set {
                url = value;
            }
        }

        /// <include file='doc\DiscoveryClientProtocol.uex' path='docs/doc[@for="DiscoveryClientResult.Filename"]/*' />
        /// <devdoc>
        ///    <para>[To be supplied.]</para>
        /// </devdoc>
        [XmlAttribute("filename")]
        public string Filename {
            get {
                return filename;
            }
            set {
                filename = value;
            }
        }
    }
}
