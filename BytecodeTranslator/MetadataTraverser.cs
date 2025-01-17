﻿//-----------------------------------------------------------------------------
//
// Copyright (C) Microsoft Corporation.  All Rights Reserved.
//
//-----------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Microsoft.Cci;
using Microsoft.Cci.MetadataReader;
using Microsoft.Cci.MutableCodeModel;
using Microsoft.Cci.Contracts;

using Bpl = Microsoft.Boogie;
using System.Diagnostics.Contracts;
using BytecodeTranslator.TranslationPlugins;

namespace BytecodeTranslator {

  /// <summary>
  /// Responsible for traversing all metadata elements (i.e., everything exclusive
  /// of method bodies).
  /// </summary>
  public class BCTMetadataTraverser : MetadataTraverser {

    readonly Sink sink;
    public readonly TraverserFactory Factory;

    public readonly IDictionary<IUnit, PdbReader> PdbReaders;
    public PdbReader/*?*/ PdbReader;

    public BCTMetadataTraverser(Sink sink, IDictionary<IUnit, PdbReader> pdbReaders, TraverserFactory factory)
      : base() {
      this.sink = sink;
      this.Factory = factory;
      this.PdbReaders = pdbReaders;
    }

    public IEnumerable<Bpl.Requires> getPreconditionTranslation(IMethodContract contract) {
      ICollection<Bpl.Requires> translatedPres = new List<Bpl.Requires>();
      foreach (IPrecondition pre in contract.Preconditions) {
        var stmtTraverser = this.Factory.MakeStatementTraverser(sink, null, true);
        ExpressionTraverser exptravers = this.Factory.MakeExpressionTraverser(sink, stmtTraverser, true);
        exptravers.Traverse(pre.Condition); // TODO
        // Todo: Deal with Descriptions
        var req = new Bpl.Requires(pre.Token(), false, exptravers.TranslatedExpressions.Pop(), "");
        translatedPres.Add(req);
      }

      return translatedPres;
    }

    public IEnumerable<Bpl.Ensures> getPostconditionTranslation(IMethodContract contract) {
      ICollection<Bpl.Ensures> translatedPosts = new List<Bpl.Ensures>();
      foreach (IPostcondition post in contract.Postconditions) {
        var stmtTraverser = this.Factory.MakeStatementTraverser(sink, null, true);
        ExpressionTraverser exptravers = this.Factory.MakeExpressionTraverser(sink, stmtTraverser, true);
        exptravers.Traverse(post.Condition);
        // Todo: Deal with Descriptions
        var ens = new Bpl.Ensures(post.Token(), false, exptravers.TranslatedExpressions.Pop(), "");
        translatedPosts.Add(ens);
      }

      return translatedPosts;
    }

    public IEnumerable<Bpl.IdentifierExpr> getModifiedIdentifiers(IMethodContract contract) {
      ICollection<Bpl.IdentifierExpr> modifiedExpr = new List<Bpl.IdentifierExpr>();
      foreach (IAddressableExpression mod in contract.ModifiedVariables) {
        ExpressionTraverser exptravers = this.Factory.MakeExpressionTraverser(sink, null, true);
        exptravers.Traverse(mod);
        Bpl.IdentifierExpr idexp = exptravers.TranslatedExpressions.Pop() as Bpl.IdentifierExpr;
        if (idexp == null) {
          throw new TranslationException(String.Format("Cannot create IdentifierExpr for Modifyed Variable {0}", mod.ToString()));
        }
        modifiedExpr.Add(idexp);
      }

      return modifiedExpr;
    }


    #region Overrides

    public override void TraverseChildren(IModule module) {
      this.PdbReaders.TryGetValue(module, out this.PdbReader);
      if (!(module.EntryPoint is Dummy))
        this.entryPoint = module.EntryPoint;

      base.TraverseChildren(module);
    }

    public override void TraverseChildren(IAssembly assembly) {
      this.PdbReaders.TryGetValue(assembly, out this.PdbReader);
      this.sink.BeginAssembly(assembly);
      try {
        base.TraverseChildren(assembly);
      } finally {
        this.sink.EndAssembly(assembly);
      }
    }

    /// <summary>
    /// Translate the type definition.
    /// </summary>
    /// 
    public override void TraverseChildren(ITypeDefinition typeDefinition) {

      if (!this.sink.TranslateType(typeDefinition)) return;

      var savedPrivateTypes = this.privateTypes;
      this.privateTypes = new List<ITypeDefinition>();

      

      var gtp = typeDefinition as IGenericTypeParameter;
      if (gtp != null) {
        return;
      }

      if (typeDefinition.IsClass) {
        bool savedSawCctor = this.sawCctor;
        this.sawCctor = false;
        var tinfo = sink.FindOrDefineType(typeDefinition);
        base.TraverseChildren(typeDefinition);
        if (!this.sawCctor) {
          CreateStaticConstructor(typeDefinition);
        }
        this.sawCctor = savedSawCctor;
        // Subtyping info
        if(sink.Options.typeInfo > 0) sink.DeclareParentsNew(typeDefinition, tinfo.Constructor);
      } else if (typeDefinition.IsDelegate) {
        ITypeDefinition unspecializedType = Microsoft.Cci.MutableContracts.ContractHelper.Unspecialized(typeDefinition).ResolvedType;
        sink.AddDelegateType(unspecializedType);
      } else if (typeDefinition.IsInterface) {
        sink.FindOrCreateTypeReference(typeDefinition);
        base.TraverseChildren(typeDefinition);
      } else if (typeDefinition.IsEnum) {
        return; // enums just are translated as ints
      } else if (typeDefinition.IsStruct) {
        sink.FindOrCreateTypeReference(typeDefinition);
        CreateDefaultStructConstructor(typeDefinition);
        CreateStructCopyConstructor(typeDefinition);
        base.TraverseChildren(typeDefinition);
      } else {
        Console.WriteLine("Unknown kind of type definition '{0}' was found",
          TypeHelper.GetTypeName(typeDefinition));
        throw new NotImplementedException(String.Format("Unknown kind of type definition '{0}'.", TypeHelper.GetTypeName(typeDefinition)));
      }
      this.Traverse(typeDefinition.PrivateHelperMembers);
      foreach (var t in this.privateTypes) {
        this.Traverse(t);
      }
    }
    List<ITypeDefinition> privateTypes = new List<ITypeDefinition>();

    

   

    /*
    private void translateAnonymousControlsForPage(ITypeDefinition typeDef) {
      if (PhoneCodeHelper.instance().PhonePlugin != null && typeDef.isPhoneApplicationPageClass(sink.host)) {
        IEnumerable<ControlInfoStructure> pageCtrls= PhoneCodeHelper.instance().PhonePlugin.getControlsForPage(typeDef.ToString());
        foreach (ControlInfoStructure ctrlInfo in pageCtrls) {
          if (ctrlInfo.Name.Contains(PhoneControlsPlugin.BOOGIE_DUMMY_CONTROL) || ctrlInfo.Name == Dummy.Name.Value) {
            string anonymousControlName = ctrlInfo.Name;
            IFieldDefinition fieldDef = new FieldDefinition() {
              ContainingTypeDefinition = typeDef,
              Name = sink.host.NameTable.GetNameFor(anonymousControlName),
              InternFactory = sink.host.InternFactory,
              Visibility = TypeMemberVisibility.Public,
              Type = sink.host.PlatformType.SystemObject,
              IsStatic = false,
            };
            (typeDef as Microsoft.Cci.MutableCodeModel.NamespaceTypeDefinition).Fields.Add(fieldDef);
            //sink.FindOrCreateFieldVariable(fieldDef);
          }
        }
      }
    }
     */ 

    private void CreateDefaultStructConstructor(ITypeDefinition typeDefinition) {
      Contract.Requires(typeDefinition.IsStruct);

      var proc = this.sink.FindOrCreateProcedureForDefaultStructCtor(typeDefinition);

      this.sink.BeginMethod(typeDefinition);
      var stmtTranslator = this.Factory.MakeStatementTraverser(this.sink, this.PdbReader, false);
      var stmts = new List<IStatement>();

      foreach (var f in typeDefinition.Fields) {
        if (f.IsStatic) continue;
        var s = new ExpressionStatement() {
          Expression = new Assignment() {
            Source = new DefaultValue() { DefaultValueType = f.Type, Type = f.Type, },
            Target = new TargetExpression() {
              Definition = f,
              Instance = new ThisReference() { Type = typeDefinition, },
              Type = f.Type,
            },
            Type = f.Type,
          },
        };
        stmts.Add(s);
      }

      stmtTranslator.Traverse(stmts);
      var translatedStatements = stmtTranslator.StmtBuilder.Collect(Bpl.Token.NoToken);

      var lit = Bpl.Expr.Literal(1);
      lit.Type = Bpl.Type.Int;
      var args = new List<object> { lit };
      var attrib = new Bpl.QKeyValue(typeDefinition.Token(), "inline", args, null); // TODO: Need to have it be {:inine 1} (and not just {:inline})?

      List<Bpl.Variable> vars = new List<Bpl.Variable>();
      foreach (Bpl.Variable v in this.sink.LocalVarMap.Values) {
        vars.Add(v);
      }
      List<Bpl.Variable> vseq = new List<Bpl.Variable>(vars.ToArray());

      Bpl.Implementation impl =
        new Bpl.Implementation(Bpl.Token.NoToken,
        proc.Name,
        new List<Bpl.TypeVariable>(),
        proc.InParams,
        proc.OutParams,
        vseq,
        translatedStatements,
        attrib,
        new Bpl.Errors()
        );

      impl.Proc = (Bpl.Procedure) proc; // TODO: get rid of cast
      this.sink.TranslatedProgram.AddTopLevelDeclaration(impl);
    }

    private void CreateStructCopyConstructor(ITypeDefinition typeDefinition) {
      Contract.Requires(typeDefinition.IsStruct);

      var proc = this.sink.FindOrCreateProcedureForStructCopy(typeDefinition);

      var stmtBuilder = new Bpl.StmtListBuilder();

      var tok = Bpl.Token.NoToken;

      var o = Bpl.Expr.Ident(proc.OutParams[0]);

      // other := Alloc();
      stmtBuilder.Add(new Bpl.CallCmd(tok, this.sink.AllocationMethodName, new List<Bpl.Expr>(), new List<Bpl.IdentifierExpr>(new Bpl.IdentifierExpr[] {o})));
      // assume DynamicType(other) == DynamicType(this);
      stmtBuilder.Add(new Bpl.AssumeCmd(tok, Bpl.Expr.Binary(Bpl.BinaryOperator.Opcode.Eq, this.sink.Heap.DynamicType(o), this.sink.Heap.DynamicType(Bpl.Expr.Ident(proc.InParams[0])))));

      var localVars = new List<Bpl.Variable>();

      foreach (var f in typeDefinition.Fields) {
        if (f.IsStatic) continue;

        var fExp = Bpl.Expr.Ident(this.sink.FindOrCreateFieldVariable(f));
        var boogieType = sink.CciTypeToBoogie(f.Type);

        if (TranslationHelper.IsStruct(f.Type)) {
          // generate a call to the copy constructor to copy the contents of f
          var proc2 = this.sink.FindOrCreateProcedureForStructCopy(f.Type);
          var e = this.sink.Heap.ReadHeap(Bpl.Expr.Ident(proc.InParams[0]), fExp, AccessType.Struct, boogieType);
          var bplLocal = this.sink.CreateFreshLocal(f.Type);
          var localExpr = Bpl.Expr.Ident(bplLocal);
          localVars.Add(bplLocal);
          var cmd = new Bpl.CallCmd(tok, proc2.Name, new List<Bpl.Expr> { e, }, new List<Bpl.IdentifierExpr>{ localExpr, });
          stmtBuilder.Add(cmd);
          this.sink.Heap.WriteHeap(tok, o, fExp, localExpr, AccessType.Struct, boogieType, stmtBuilder);
        } else {
          // just generate a normal assignment to the field f
          var e = this.sink.Heap.ReadHeap(Bpl.Expr.Ident(proc.InParams[0]), fExp, AccessType.Struct, boogieType);
          this.sink.Heap.WriteHeap(tok, o, fExp, e, AccessType.Struct, boogieType, stmtBuilder);
        }
      }

      var lit = Bpl.Expr.Literal(1);
      lit.Type = Bpl.Type.Int;
      var args = new List<object> { lit };
      var attrib = new Bpl.QKeyValue(typeDefinition.Token(), "inline", args, null);
      Bpl.Implementation impl =
        new Bpl.Implementation(Bpl.Token.NoToken,
        proc.Name,
        new List<Bpl.TypeVariable>(),
        proc.InParams,
        proc.OutParams,
        localVars,
        stmtBuilder.Collect(Bpl.Token.NoToken),
        attrib,
        new Bpl.Errors()
        );

      impl.Proc = (Bpl.Procedure)proc; // TODO: get rid of cast
      this.sink.TranslatedProgram.AddTopLevelDeclaration(impl);
    }

    private bool sawCctor = false;
    private IMethodReference/*?*/ entryPoint = null;

    private void CreateStaticConstructor(ITypeDefinition typeDefinition) {
      var typename = TypeHelper.GetTypeName(typeDefinition, Microsoft.Cci.NameFormattingOptions.DocumentationId);
      typename = TranslationHelper.TurnStringIntoValidIdentifier(typename);
      var proc = new Bpl.Procedure(Bpl.Token.NoToken, typename + ".#cctor",
          new List<Bpl.TypeVariable>(),
          new List<Bpl.Variable>(), // in
          new List<Bpl.Variable>(), // out
          new List<Bpl.Requires>(),
          new List<Bpl.IdentifierExpr>(), // modifies
          new List<Bpl.Ensures>()
          );

      this.sink.TranslatedProgram.AddTopLevelDeclaration(proc);

      this.sink.BeginMethod(typeDefinition);

      var stmtTranslator = this.Factory.MakeStatementTraverser(this.sink, this.PdbReader, false);
      var stmts = new List<IStatement>();

      foreach (var f in typeDefinition.Fields) {
        if (!f.IsStatic) continue;
        stmts.Add(
          new ExpressionStatement() {
            Expression = new Assignment() {
              Source = new DefaultValue() { DefaultValueType = f.Type, Type = f.Type, },
              Target = new TargetExpression() {
                Definition = f,
                Instance = null,
                Type = f.Type,
              },
              Type = f.Type,
            }
          });
      }

      stmtTranslator.Traverse(stmts);
      var translatedStatements = stmtTranslator.StmtBuilder.Collect(Bpl.Token.NoToken);

      List<Bpl.Variable> vars = new List<Bpl.Variable>();
      foreach (Bpl.Variable v in this.sink.LocalVarMap.Values) {
        vars.Add(v);
      }
      List<Bpl.Variable> vseq = new List<Bpl.Variable>(vars.ToArray());

      Bpl.Implementation impl =
        new Bpl.Implementation(Bpl.Token.NoToken,
        proc.Name,
        new List<Bpl.TypeVariable>(),
        proc.InParams,
        proc.OutParams,
        vseq,
        translatedStatements
        );

      impl.Proc = proc;
      this.sink.TranslatedProgram.AddTopLevelDeclaration(impl);

    }

    /// <summary>
    /// 
    /// </summary>
    public override void TraverseChildren(IMethodDefinition method) {

      if (method.IsStaticConstructor) this.sawCctor = true;

      bool isEventAddOrRemove = method.IsSpecialName && (method.Name.Value.StartsWith("add_") || method.Name.Value.StartsWith("remove_"));
      if (isEventAddOrRemove)
        return;


      Sink.ProcedureInfo procInfo;
      IMethodDefinition stubMethod = null;
      if (IsStubMethod(method, out stubMethod)) {
        procInfo = this.sink.FindOrCreateProcedure(stubMethod);
      } else {
        procInfo = this.sink.FindOrCreateProcedure(method);
      }

      if (method.IsAbstract || method.IsExternal) { // we're done, just define the procedure
        return;
      }

      this.sink.BeginMethod(method);
      var decl = procInfo.Decl;
      var proc = decl as Bpl.Procedure;
      var formalMap = procInfo.FormalMap;

      if (this.entryPoint != null && method.InternedKey == this.entryPoint.InternedKey) {
        decl.AddAttribute("entrypoint");
      }

      

      try {
        StatementTraverser stmtTraverser = this.Factory.MakeStatementTraverser(this.sink, this.PdbReader, false);

        // FEEDBACK if this is a feedback method it will be plagued with false asserts. They will trigger if $Exception becomes other than null
        // FEEDBACK for modular analysis we need it to be non-null at the start
        // FEEDBACK also, callee is obviously non null
        IMethodDefinition translatedMethod= sink.getMethodBeingTranslated();
        

        #region Add assignments from In-Params to local-Params

        foreach (MethodParameter mparam in formalMap) {
          if (mparam.inParameterCopy != null) {
            Bpl.IToken tok = method.Token();
            stmtTraverser.StmtBuilder.Add(Bpl.Cmd.SimpleAssign(tok,
              new Bpl.IdentifierExpr(tok, mparam.outParameterCopy),
              new Bpl.IdentifierExpr(tok, mparam.inParameterCopy)));
          }
        }

        #endregion

        #region For non-deferring ctors and all cctors, initialize all fields to null-equivalent values
        var inits = InitializeFieldsInConstructor(method);
        if (0 < inits.Count) {
          foreach (var s in inits) {
            stmtTraverser.Traverse(s);
          }
        }
        #endregion

        #region Translate method attributes
        // Don't need an expression translator because there is a limited set of things
        // that can appear as arguments to custom attributes
        // TODO: decode enum values
        try {
          foreach (var a in method.Attributes) {
            var attrName = TypeHelper.GetTypeName(a.Type);
            if (attrName.EndsWith("Attribute"))
              attrName = attrName.Substring(0, attrName.Length - 9);
            var args = new List<object>();
            foreach (var c in a.Arguments) {
              var mdc = c as IMetadataConstant;
              if (mdc != null) {
                object o;
                if (mdc.Type.IsEnum) {
                  var lit = Bpl.Expr.Literal((int) mdc.Value);
                  lit.Type = Bpl.Type.Int;
                  o = lit;
                } else {
                  switch (mdc.Type.TypeCode) {
                    case PrimitiveTypeCode.Boolean:
                      o = (bool) mdc.Value ? Bpl.Expr.True : Bpl.Expr.False;
                      break;
                    case PrimitiveTypeCode.Int32:
                      var lit = Bpl.Expr.Literal((int) mdc.Value);
                      lit.Type = Bpl.Type.Int;
                      o = lit;
                      break;
                    case PrimitiveTypeCode.String:
                      o = mdc.Value;
                      break;
                    default:
                      throw new InvalidCastException("Invalid metadata constant type");
                  }
                }
                args.Add(o);
              }
            }
            decl.AddAttribute(attrName, args.ToArray());
          }
        } catch (InvalidCastException) {
          Console.WriteLine("Warning: Cannot translate custom attributes for method\n    '{0}':",
            MemberHelper.GetMethodSignature(method, NameFormattingOptions.None));
          Console.WriteLine("    >>Skipping attributes, continuing with method translation");
        }
        #endregion

        #region Translate body
        var helperTypes = stmtTraverser.TranslateMethod(method);
        if (helperTypes != null) {
          this.privateTypes.AddRange(helperTypes);
        }
        #endregion

        #region Create Local Vars For Implementation
        List<Bpl.Variable> vars = new List<Bpl.Variable>();
        foreach (MethodParameter mparam in formalMap) {
          if (!mparam.underlyingParameter.IsByReference)
            vars.Add(mparam.outParameterCopy);
        }
        foreach (Bpl.Variable v in this.sink.LocalVarMap.Values) {
          vars.Add(v);
        }
        // LocalExcVariable holds the exception thrown by any method called from this method, even if this method swallows all exceptions
        if (0 <this.sink.Options.modelExceptions)
          vars.Add(procInfo.LocalExcVariable);
        vars.Add(procInfo.LabelVariable);
        List<Bpl.Variable> vseq = new List<Bpl.Variable>(vars.ToArray());
        #endregion

        var translatedBody = stmtTraverser.StmtBuilder.Collect(Bpl.Token.NoToken);

        #region Add implementation to Boogie program
        if (proc != null) {
          Bpl.Implementation impl =
              new Bpl.Implementation(method.Token(),
                  decl.Name,
                  new List<Bpl.TypeVariable>(),
                  decl.InParams,
                  decl.OutParams,
                  vseq,
                  translatedBody);

          impl.Proc = proc;
          this.sink.TranslatedProgram.AddTopLevelDeclaration(impl);
        } else { // method is translated as a function
          //var func = decl as Bpl.Function;
          //Contract.Assume(func != null);
          //var blocks = new List<Bpl.Block>();
          //var counter = 0;
          //var returnValue = decl.OutParams[0];
          //foreach (var bb in translatedBody.BigBlocks) {
          //  var label = bb.LabelName ?? "L" + counter++.ToString();
          //  var newTransferCmd = (bb.tc is Bpl.ReturnCmd)
          //    ? new Bpl.ReturnExprCmd(bb.tc.tok, Bpl.Expr.Ident(returnValue))
          //    : bb.tc;
          //  var b = new Bpl.Block(bb.tok, label, bb.simpleCmds, newTransferCmd);
          //  blocks.Add(b);
          //}
          //var localVars = new List<Bpl.Variable>();
          //localVars.Add(returnValue);
          //func.Body = new Bpl.CodeExpr(localVars, blocks);
        }
        #endregion

      } catch (TranslationException te) {
        Console.WriteLine("Translation error in body of \n    '{0}':",
          MemberHelper.GetMethodSignature(method, NameFormattingOptions.None));
        Console.WriteLine("\t" + te.Message);
      } catch (Exception e) {
        Console.WriteLine("Error encountered during translation of \n    '{0}':",
          MemberHelper.GetMethodSignature(method, NameFormattingOptions.None));
        Console.WriteLine("\t>>" + e.Message);
      } finally {
      }
    }

    private static List<IStatement> InitializeFieldsInConstructor(IMethodDefinition method) {
      Contract.Ensures(Contract.Result<List<IStatement>>() != null);
      var inits = new List<IStatement>();
      if (method.IsConstructor || method.IsStaticConstructor) {
        var smb = method.Body as ISourceMethodBody;
        if (method.IsStaticConstructor || (smb != null && !FindCtorCall.IsDeferringCtor(method, smb.Block))) {
          var thisExp = new ThisReference() { Type = method.ContainingTypeDefinition, };
          foreach (var f in method.ContainingTypeDefinition.Fields) {
            if (f.IsStatic == method.IsStatic) {
              var a = new Assignment() {
                Source = new DefaultValue() { DefaultValueType = f.Type, Type = f.Type, },
                Target = new TargetExpression() { Definition = f, Instance = method.IsConstructor ? thisExp : null, Type = f.Type },
                Type = f.Type,
              };
              inits.Add(new ExpressionStatement() { Expression = a, });
            }
          }
        }
      }
      return inits;
    }
    // TODO: do a type test, not a string test for the attribute
    private bool IsStubMethod(IMethodDefinition methodDefinition, out IMethodDefinition/*?*/ stubMethod) {
      stubMethod = null;
      var td = GetTypeDefinitionFromAttribute(methodDefinition.Attributes, "BytecodeTranslator.StubAttribute");
      if (td == null)
        td = GetTypeDefinitionFromAttribute(methodDefinition.ContainingTypeDefinition.Attributes, "BytecodeTranslator.StubAttribute");
      if (td != null) {
        foreach (var mem in td.GetMatchingMembersNamed(methodDefinition.Name, false,
          tdm => {
            var md = tdm as IMethodDefinition;
            return md != null && MemberHelper.MethodsAreEquivalent(methodDefinition, md);
          })) {
          stubMethod = mem as IMethodDefinition;
          return true;
        }
      }
      return false;
    }
    public static ITypeDefinition/*?*/ GetTypeDefinitionFromAttribute(IEnumerable<ICustomAttribute> attributes, string attributeName) {
      ICustomAttribute foundAttribute = null;
      foreach (ICustomAttribute attribute in attributes) {
        if (TypeHelper.GetTypeName(attribute.Type) == attributeName) {
          foundAttribute = attribute;
          break;
        }
      }
      if (foundAttribute == null) return null;
      List<IMetadataExpression> args = new List<IMetadataExpression>(foundAttribute.Arguments);
      if (args.Count < 1) return null;
      IMetadataTypeOf abstractTypeMD = args[0] as IMetadataTypeOf;
      if (abstractTypeMD == null) return null;
      ITypeReference referencedTypeReference = Microsoft.Cci.MutableContracts.ContractHelper.Unspecialized(abstractTypeMD.TypeToGet);
      ITypeDefinition referencedTypeDefinition = referencedTypeReference.ResolvedType;
      return referencedTypeDefinition;
    }

    public override void TraverseChildren(IFieldDefinition fieldDefinition) {
      Bpl.Variable fieldVar= this.sink.FindOrCreateFieldVariable(fieldDefinition);

      // if tracked by the phone plugin, we need to find out the bpl assigned name for future use
      
    }

   
    #endregion

   

    #region Public API
    public virtual void TranslateAssemblies(IEnumerable<IUnit> assemblies) {
      /*
            if (PhoneCodeHelper.instance().PhonePlugin != null)
        addPhoneTopLevelDeclarations();
      */
      foreach (var a in assemblies) {
        this.Traverse((IAssembly)a);
      }
    }
    #endregion

    #region Helpers
    private class FindCtorCall : CodeTraverser {
      private bool isDeferringCtor = false;
      public ITypeReference containingType;
      public static bool IsDeferringCtor(IMethodDefinition method, IBlockStatement body) {
        var fcc = new FindCtorCall(method.ContainingType);
        fcc.Traverse(body);
        return fcc.isDeferringCtor;
      }
      private FindCtorCall(ITypeReference containingType) {
        this.containingType = containingType;
      }
      public override void TraverseChildren(IMethodCall methodCall) {
        var md = methodCall.MethodToCall.ResolvedMethod;
        if (md != null && md.IsConstructor && methodCall.ThisArgument is IThisReference) {
          this.isDeferringCtor = TypeHelper.TypesAreEquivalent(md.ContainingType, containingType);
          return;
        }
        base.TraverseChildren(methodCall);
      }
    }

    #endregion

  }
}