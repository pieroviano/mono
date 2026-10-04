//
// System.Web.Compilation.PageCompiler
//
// Authors:
//	Gonzalo Paniagua Javier (gonzalo@ximian.com)
//
// (C) 2002,2003 Ximian, Inc (http://www.ximian.com)
//

//
// Permission is hereby granted, free of charge, to any person obtaining
// a copy of this software and associated documentation files (the
// "Software"), to deal in the Software without restriction, including
// without limitation the rights to use, copy, modify, merge, publish,
// distribute, sublicense, and/or sell copies of the Software, and to
// permit persons to whom the Software is furnished to do so, subject to
// the following conditions:
// 
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
// MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
// LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
// OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
// WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
//
using System;
using System.CodeDom;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Web.Configuration;
using System.Web.UI;
using System.Web.SessionState;
using System.Web.Util;
using System.Web.Profile;

namespace System.Web.Compilation
{
	class PageCompiler : TemplateControlCompiler
	{
		PageParser pageParser;
		static CodeTypeReference intRef = new CodeTypeReference (typeof (int));

		public PageCompiler (PageParser pageParser)
			: base (pageParser)
		{
			this.pageParser = pageParser;
		}

		protected override void CreateStaticFields ()
		{
			base.CreateStaticFields ();
			
			var fld = new CodeMemberField (typeof (object), "__fileDependencies");
			fld.Attributes = MemberAttributes.Private | MemberAttributes.Static;
			fld.InitExpression = new CodePrimitiveExpression (null);
			mainClass.Members.Add (fld);

			if (pageParser.OutputCache) {
				fld = new CodeMemberField (typeof (OutputCacheParameters), "__outputCacheSettings");
				fld.Attributes = MemberAttributes.Private | MemberAttributes.Static;
				fld.InitExpression = new CodePrimitiveExpression (null);
				mainClass.Members.Add (fld);
			}
		}
		
		protected override void CreateConstructor (CodeStatementCollection? localVars,
							   CodeStatementCollection? trueStmt)
		{
			var masterPageFile = pageParser.MasterPageFile;
			if (masterPageFile != null && !masterPageFile.IsExpression)
				// This is here just to trigger master page build, so that its type
				// is available when compiling the page itself.
				BuildManager.GetCompiledType (masterPageFile.Value);

			MainDirectiveAttribute <string> clientTarget;
			clientTarget = pageParser.ClientTarget;
			if (clientTarget != null) {
				CodeExpression prop;
				prop = new CodePropertyReferenceExpression (thisRef, "ClientTarget");
				CodeExpression ct = null;

				if (clientTarget.IsExpression) {
					var pi = GetFieldOrProperty (typeof (Page), "ClientTarget") as PropertyInfo;
					if (pi != null)
						ct = CompileExpression (pi, pi.PropertyType, clientTarget.UnparsedValue, false);
				}

				if (ct == null)
					ct = new CodePrimitiveExpression (clientTarget.Value);
				if (localVars == null)
					localVars = new CodeStatementCollection ();
				localVars.Add (new CodeAssignStatement (prop, ct));
			}

			var deps = pageParser.Dependencies;
			var depsCount = deps != null ? deps.Count : 0;
			
			if (depsCount > 0) {
				if (localVars == null)
					localVars = new CodeStatementCollection ();
				if (trueStmt == null)
					trueStmt = new CodeStatementCollection ();

				CodeAssignStatement assign;
				localVars.Add (
					new CodeVariableDeclarationStatement (
						typeof (string[]),
						"dependencies")
				);

				var dependencies = new CodeVariableReferenceExpression ("dependencies");
				trueStmt.Add (
					new CodeAssignStatement (dependencies, new CodeArrayCreateExpression (typeof (string), depsCount))
				);
				
				CodeArrayIndexerExpression arrayIndex;
				object o;
				
				for (var i = 0; i < depsCount; i++) {
					o = deps [i];
					arrayIndex = new CodeArrayIndexerExpression (dependencies, new CodeExpression[] {new CodePrimitiveExpression (i)});
					assign = new CodeAssignStatement (arrayIndex, new CodePrimitiveExpression (o));
					trueStmt.Add (assign);
				}
				
				var getDepsCall = new CodeMethodInvokeExpression (
					thisRef,
					"GetWrappedFileDependencies",
					new CodeExpression[] {dependencies}
				);
				assign = new CodeAssignStatement (GetMainClassFieldReferenceExpression ("__fileDependencies"), getDepsCall);

				trueStmt.Add (assign);
			}

			base.CreateConstructor (localVars, trueStmt);
		}
		
		protected override void AddInterfaces () 
		{
			base.AddInterfaces ();
			CodeTypeReference cref;
			
			if (pageParser.EnableSessionState) {
				cref = new CodeTypeReference (typeof (IRequiresSessionState));
				if (partialClass != null)
					partialClass.BaseTypes.Add (cref);
				else
					mainClass.BaseTypes.Add (cref);
			}
			
			if (pageParser.ReadOnlySessionState) {
				cref = new CodeTypeReference (typeof (IReadOnlySessionState));
				if (partialClass != null)
					partialClass.BaseTypes.Add (cref);					
				else
					mainClass.BaseTypes.Add (cref);
			}

			if (pageParser.Async)
				mainClass.BaseTypes.Add (new CodeTypeReference (typeof (System.Web.IHttpAsyncHandler)));
			
			mainClass.BaseTypes.Add (new CodeTypeReference (typeof (System.Web.IHttpHandler)));
		}

		void CreateGetTypeHashCode () 
		{
			var method = new CodeMemberMethod ();
			method.ReturnType = intRef;
			method.Name = "GetTypeHashCode";
			method.Attributes = MemberAttributes.Public | MemberAttributes.Override;
			var rnd = new Random (pageParser.InputFile.GetHashCode ());
			method.Statements.Add (new CodeMethodReturnStatement (new CodePrimitiveExpression (rnd.Next ())));
			mainClass.Members.Add (method);
		}

		static CodeExpression GetExpressionForValueAndType (object value, Type valueType)
		{
			// Put short circuit types here
			if (valueType == typeof (TimeSpan)) {
				var mref = new CodeMethodReferenceExpression (
					new CodeTypeReferenceExpression (typeof (TimeSpan)),
					"Parse");

				return new CodeMethodInvokeExpression (
					mref,
					new CodeExpression[] { new CodePrimitiveExpression (((TimeSpan) value).ToString ()) }
				);
			}

			throw new HttpException (String.Format ("Unable to create assign expression for type '{0}'.", valueType));
		}

		static CodeAssignStatement CreatePropertyAssign (CodeExpression owner, string name, CodeExpression rhs)
		{
			return new CodeAssignStatement (new CodePropertyReferenceExpression (owner, name), rhs);
		}
		
		static CodeAssignStatement CreatePropertyAssign (CodeExpression owner, string name, object? value)
		{
			CodeExpression rhs;
			if (value == null || value is string)
				rhs = new CodePrimitiveExpression (value);
			else {
				var vt = value.GetType ();

				if (vt.IsPrimitive)
					rhs = new CodePrimitiveExpression (value);
				else
					rhs = GetExpressionForValueAndType (value, vt);
			}
			
			return CreatePropertyAssign (owner, name, rhs);
		}

		static CodeAssignStatement CreatePropertyAssign (string name, object? value)
		{
			return CreatePropertyAssign (thisRef, name, value);
		}

		void AssignPropertyWithExpression <T> (CodeMemberMethod method, string name, MainDirectiveAttribute <T>? value, ILocation? location)
		{
			if (value == null)
				return;
			CodeAssignStatement assign;
			CodeExpression rhs = null;
			
			if (value.IsExpression) {
				var pi = GetFieldOrProperty (typeof (Page), name) as PropertyInfo;
				if (pi != null)
					rhs = CompileExpression (pi, pi.PropertyType, value.UnparsedValue, false);
			}
			
			if (rhs != null)
				assign = CreatePropertyAssign (thisRef, name, rhs);
			else
				assign = CreatePropertyAssign (name, value.Value);

			method.Statements.Add (AddLinePragma (assign, location));
		}
		
		void AddStatementsFromDirective (ControlBuilder builder, CodeMemberMethod method, ILocation? location)
		{
			AssignPropertyWithExpression <string> (method, "ResponseEncoding", pageParser.ResponseEncoding, location);
			AssignPropertyWithExpression <int> (method, "CodePage", pageParser.CodePage, location);
			AssignPropertyWithExpression <int> (method, "LCID", pageParser.LCID, location);

			var contentType = pageParser.ContentType;
			if (contentType != null)
				method.Statements.Add (AddLinePragma (CreatePropertyAssign ("ContentType", contentType), location));

			var culture = pageParser.Culture;
			if (culture != null)
				method.Statements.Add (AddLinePragma (CreatePropertyAssign ("Culture", culture), location));

			culture = pageParser.UICulture;
			if (culture != null)
				method.Statements.Add (AddLinePragma (CreatePropertyAssign ("UICulture", culture), location));

			var errorPage = pageParser.ErrorPage;
			if (errorPage != null)
				method.Statements.Add (AddLinePragma (CreatePropertyAssign ("ErrorPage", errorPage), location));

                        if (pageParser.HaveTrace) {
                                var stmt = new CodeAssignStatement ();
                                stmt.Left = new CodePropertyReferenceExpression (thisRef, "TraceEnabled");
                                stmt.Right = new CodePrimitiveExpression (pageParser.Trace);
                                method.Statements.Add (AddLinePragma (stmt, location));
                        }

                        if (pageParser.TraceMode != TraceMode.Default) {
                                var stmt = new CodeAssignStatement ();
                                var tm = new CodeTypeReferenceExpression ("System.Web.TraceMode");
                                stmt.Left = new CodePropertyReferenceExpression (thisRef, "TraceModeValue");
                                stmt.Right = new CodeFieldReferenceExpression (tm, pageParser.TraceMode.ToString ());
                                method.Statements.Add (AddLinePragma (stmt, location));
                        }

                        if (pageParser.NotBuffer) {
                                var stmt = new CodeAssignStatement ();
                                stmt.Left = new CodePropertyReferenceExpression (thisRef, "Buffer");
                                stmt.Right = new CodePrimitiveExpression (false);
                                method.Statements.Add (AddLinePragma (stmt, location));
                        }

			if (!pageParser.EnableEventValidation) {
                                var stmt = new CodeAssignStatement ();
                                CodePropertyReferenceExpression prop;
                                prop = new CodePropertyReferenceExpression (thisRef, "EnableEventValidation");
				stmt.Left = prop;
				stmt.Right = new CodePrimitiveExpression (pageParser.EnableEventValidation);
				method.Statements.Add (AddLinePragma (stmt, location));
			}

			if (pageParser.MaintainScrollPositionOnPostBack) {
				var stmt = new CodeAssignStatement ();
				CodePropertyReferenceExpression prop;
                                prop = new CodePropertyReferenceExpression (thisRef, "MaintainScrollPositionOnPostBack");
				stmt.Left = prop;
				stmt.Right = new CodePrimitiveExpression (pageParser.MaintainScrollPositionOnPostBack);
				method.Statements.Add (AddLinePragma (stmt, location));
			}
		}

		protected override void AddStatementsToConstructor (CodeConstructor ctor)
		{
			base.AddStatementsToConstructor (ctor);
			if (pageParser.OutputCache)
				OutputCacheParamsBlock (ctor);
		}
		
		protected override void AddStatementsToInitMethodTop (ControlBuilder builder, CodeMemberMethod method)
		{
			base.AddStatementsToInitMethodTop (builder, method);
			
			var directiveLocation = pageParser.DirectiveLocation;
			AddStatementsFromDirective (builder, method, directiveLocation);

			var ctrlVar = new CodeArgumentReferenceExpression("__ctrl");
			if (pageParser.EnableViewStateMacSet)
				method.Statements.Add (AddLinePragma (CreatePropertyAssign (ctrlVar, "EnableViewStateMac", pageParser.EnableViewStateMacSet), directiveLocation));

			AssignPropertyWithExpression <string> (method, "Title", pageParser.Title, directiveLocation);
			AssignPropertyWithExpression <string> (method, "MasterPageFile", pageParser.MasterPageFile, directiveLocation);
			AssignPropertyWithExpression <string> (method, "Theme", pageParser.Theme, directiveLocation);

			if (pageParser.StyleSheetTheme != null)
				method.Statements.Add (AddLinePragma (CreatePropertyAssign (ctrlVar, "StyleSheetTheme", pageParser.StyleSheetTheme), directiveLocation));

			if (pageParser.Async != false)
				method.Statements.Add (AddLinePragma (CreatePropertyAssign (ctrlVar, "AsyncMode", pageParser.Async), directiveLocation));

			if (pageParser.AsyncTimeout != -1)
				method.Statements.Add (AddLinePragma (CreatePropertyAssign (ctrlVar, "AsyncTimeout",
											    TimeSpan.FromSeconds (pageParser.AsyncTimeout)), directiveLocation));

			var expr = new CodeMethodInvokeExpression (thisRef, "InitializeCulture");
			method.Statements.Add (AddLinePragma (new CodeExpressionStatement (expr), directiveLocation));
		}
		protected override void AddStatementsToInitMethodBottom (ControlBuilder builder, CodeMemberMethod method)
		{
			var directiveLocation = pageParser.DirectiveLocation;
			AssignPropertyWithExpression <string> (method, "MetaDescription", pageParser.MetaDescription, directiveLocation);
			AssignPropertyWithExpression <string> (method, "MetaKeywords", pageParser.MetaKeywords, directiveLocation);
		}
		protected override void PrependStatementsToFrameworkInitialize (CodeMemberMethod method)
		{
			base.PrependStatementsToFrameworkInitialize (method);
			if (pageParser.StyleSheetTheme != null)
				method.Statements.Add (CreatePropertyAssign ("StyleSheetTheme", pageParser.StyleSheetTheme));
		}
		
		protected override void AppendStatementsToFrameworkInitialize (CodeMemberMethod method)
		{
			base.AppendStatementsToFrameworkInitialize (method);

			var deps = pageParser.Dependencies;
			var depsCount = deps != null ? deps.Count : 0;

			if (depsCount > 0) {
				var fileDependencies = GetMainClassFieldReferenceExpression ("__fileDependencies");

				method.Statements.Add (
					new CodeMethodInvokeExpression (
						thisRef,
						"AddWrappedFileDependencies",
						new CodeExpression[] {fileDependencies})
				);

			}

			if (pageParser.OutputCache) {
				var init = new CodeMethodReferenceExpression (thisRef, "InitOutputCache");
				var invoke = new CodeMethodInvokeExpression (init, GetMainClassFieldReferenceExpression ("__outputCacheSettings"));
				method.Statements.Add (invoke);
			}

			if (pageParser.ValidateRequest) {
				var expr = new CodeMethodInvokeExpression ();
                                CodePropertyReferenceExpression prop;
                                prop = new CodePropertyReferenceExpression (thisRef, "Request");
				expr.Method = new CodeMethodReferenceExpression (prop, "ValidateInput");
				method.Statements.Add (expr);
			}
		}

		CodeAssignStatement AssignOutputCacheParameter (CodeVariableReferenceExpression variable, string propName, object value)
		{
			var ret = new CodeAssignStatement ();

			ret.Left = new CodeFieldReferenceExpression (variable, propName);

			if (value is OutputCacheLocation)
				ret.Right = new CodeFieldReferenceExpression (
					new CodeTypeReferenceExpression (new CodeTypeReference (typeof (OutputCacheLocation), CodeTypeReferenceOptions.GlobalReference)),
					value.ToString ()
				);
			else
				ret.Right = new CodePrimitiveExpression (value);
			return ret;
		}
		
		void OutputCacheParamsBlock (CodeMemberMethod method)
		{
			var statements = new List <CodeStatement> ();
			var localSettingsDecl = new CodeVariableDeclarationStatement (typeof (OutputCacheParameters), "outputCacheSettings");
			var localSettings = new CodeVariableReferenceExpression ("outputCacheSettings");
			
			statements.Add (localSettingsDecl);
			statements.Add (
				new CodeAssignStatement (
					localSettings,
					new CodeObjectCreateExpression (typeof (OutputCacheParameters), new CodeExpression[] {})
				)
			);
			
			var parsed = pageParser.OutputCacheParsedParameters;
			if ((parsed & TemplateParser.OutputCacheParsedParams.CacheProfile) != 0)
				statements.Add (AssignOutputCacheParameter (localSettings, "CacheProfile", pageParser.OutputCacheCacheProfile));
			statements.Add (AssignOutputCacheParameter (localSettings, "Duration", pageParser.OutputCacheDuration));
			if ((parsed & TemplateParser.OutputCacheParsedParams.Location) != 0)
				statements.Add (AssignOutputCacheParameter (localSettings, "Location", pageParser.OutputCacheLocation));
			if ((parsed & TemplateParser.OutputCacheParsedParams.NoStore) != 0)
				statements.Add (AssignOutputCacheParameter (localSettings, "NoStore", pageParser.OutputCacheNoStore));
			if ((parsed & TemplateParser.OutputCacheParsedParams.SqlDependency) != 0)
				statements.Add (AssignOutputCacheParameter (localSettings, "SqlDependency", pageParser.OutputCacheSqlDependency));
			if ((parsed & TemplateParser.OutputCacheParsedParams.VaryByContentEncodings) != 0)
				statements.Add (AssignOutputCacheParameter (localSettings, "VaryByContentEncoding", pageParser.OutputCacheVaryByContentEncodings));
			if ((parsed & TemplateParser.OutputCacheParsedParams.VaryByControl) != 0)
				statements.Add (AssignOutputCacheParameter (localSettings, "VaryByControl", pageParser.OutputCacheVaryByControls));
			if ((parsed & TemplateParser.OutputCacheParsedParams.VaryByCustom) != 0)
				statements.Add (AssignOutputCacheParameter (localSettings, "VaryByCustom", pageParser.OutputCacheVaryByCustom));
			if ((parsed & TemplateParser.OutputCacheParsedParams.VaryByHeader) != 0)
				statements.Add (AssignOutputCacheParameter (localSettings, "VaryByHeader", pageParser.OutputCacheVaryByHeader));
			statements.Add (AssignOutputCacheParameter (localSettings, "VaryByParam", pageParser.OutputCacheVaryByParam));

			var outputCacheSettings = GetMainClassFieldReferenceExpression ("__outputCacheSettings");
			statements.Add (new CodeAssignStatement (outputCacheSettings, localSettings));
			
			var cond = new CodeConditionStatement (
				new CodeBinaryOperatorExpression (
					outputCacheSettings,
					CodeBinaryOperatorType.IdentityEquality,
					new CodePrimitiveExpression (null)
				),
				statements.ToArray ()
			);

			method.Statements.Add (cond);
		}

		void CreateStronglyTypedProperty (Type? type, string name)
		{
			if (type == null)
				return;
			
			var mprop = new CodeMemberProperty ();
			mprop.Name = name;
			mprop.Type = new CodeTypeReference (type);
			mprop.Attributes = MemberAttributes.Public | MemberAttributes.New;
			CodeExpression prop = new CodePropertyReferenceExpression (new CodeBaseReferenceExpression (), name);
			prop = new CodeCastExpression (type, prop);
			mprop.GetStatements.Add (new CodeMethodReturnStatement (prop));
			if (partialClass != null)
				partialClass.Members.Add (mprop);
			else
				mainClass.Members.Add (mprop);

			AddReferencedAssembly (type.Assembly);
		}
		
		protected internal override void CreateMethods ()
		{
			base.CreateMethods ();

			CreateProfileProperty ();
			CreateStronglyTypedProperty (pageParser.MasterType, "Master");
			CreateStronglyTypedProperty (pageParser.PreviousPageType, "PreviousPage");
			CreateGetTypeHashCode ();

			if (pageParser.Async)
				CreateAsyncMethods ();
		}

		void CreateAsyncMethods ()
		{
			var method = new CodeMemberMethod ();
			CodeParameterDeclarationExpression arg;
			CodeMethodInvokeExpression invoke;

			// public virtual System.IAsyncResult BeginProcessRequest(System.Web.HttpContext context, System.AsyncCallback cb, object data);
			method.ReturnType = new CodeTypeReference (typeof (IAsyncResult));
			method.Name = "BeginProcessRequest";
			method.Attributes = MemberAttributes.Public;
			
			arg = new CodeParameterDeclarationExpression ();
			arg.Type = new CodeTypeReference (typeof (HttpContext));
			arg.Name = "context";
			method.Parameters.Add (arg);

			arg = new CodeParameterDeclarationExpression ();
			arg.Type = new CodeTypeReference (typeof (AsyncCallback));
			arg.Name = "cb";
			method.Parameters.Add (arg);

			arg = new CodeParameterDeclarationExpression ();
			arg.Type = new CodeTypeReference (typeof (object));
			arg.Name = "data";
			method.Parameters.Add (arg);

			invoke = new CodeMethodInvokeExpression (thisRef, "AsyncPageBeginProcessRequest");
			invoke.Parameters.Add (new CodeArgumentReferenceExpression ("context"));
			invoke.Parameters.Add (new CodeArgumentReferenceExpression ("cb"));
			invoke.Parameters.Add (new CodeArgumentReferenceExpression ("data"));

			method.Statements.Add (new CodeMethodReturnStatement (invoke));
			mainClass.Members.Add (method);

			// public virtual void EndProcessRequest(System.IAsyncResult ar);
			method = new CodeMemberMethod ();
			method.ReturnType = new CodeTypeReference (typeof (void));
			method.Name = "EndProcessRequest";
			method.Attributes = MemberAttributes.Public;

			arg = new CodeParameterDeclarationExpression ();
			arg.Type = new CodeTypeReference (typeof (IAsyncResult));
			arg.Name = "ar";
			method.Parameters.Add (arg);

			invoke = new CodeMethodInvokeExpression (thisRef, "AsyncPageEndProcessRequest");
			invoke.Parameters.Add (new CodeArgumentReferenceExpression ("ar"));

			method.Statements.Add (invoke);
			mainClass.Members.Add (method);

			// public override void ProcessRequest(System.Web.HttpContext context);
			method = new CodeMemberMethod ();
			method.ReturnType = new CodeTypeReference (typeof (void));
			method.Name = "ProcessRequest";
			method.Attributes = MemberAttributes.Public | MemberAttributes.Override;

			arg = new CodeParameterDeclarationExpression ();
			arg.Type = new CodeTypeReference (typeof (HttpContext));
			arg.Name = "context";
			method.Parameters.Add (arg);
			
			invoke = new CodeMethodInvokeExpression (new CodeBaseReferenceExpression (), "ProcessRequest");
			invoke.Parameters.Add (new CodeArgumentReferenceExpression ("context"));

			method.Statements.Add (invoke);
			mainClass.Members.Add (method);
		}
		
		public static Type CompilePageType (PageParser pageParser)
		{
			var compiler = new PageCompiler (pageParser);
			return compiler.GetCompiledType ();
		}
	}
}
