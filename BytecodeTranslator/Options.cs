//-----------------------------------------------------------------------------
//
// Copyright (C) Microsoft Corporation.  All Rights Reserved.
//
//-----------------------------------------------------------------------------

using System;
using System.Linq;
using System.IO;
using Microsoft.Cci;
using Microsoft.Cci.MetadataReader;
using Microsoft.Cci.MutableCodeModel;
using System.Collections.Generic;
using Microsoft.Cci.Contracts;
using Microsoft.Cci.ILToCodeModel;
using Microsoft.Cci.MutableContracts;

using Bpl = Microsoft.Boogie;
using System.Diagnostics.Contracts;
using Microsoft.Cci.MutableCodeModel.Contracts;
using System.Text.RegularExpressions;
using BytecodeTranslator.TranslationPlugins;
using BytecodeTranslator.TranslationPlugins.BytecodeTranslator;

namespace BytecodeTranslator
{

    public class Options : OptionParsing
    {

        [OptionDescription("The names of the assemblies to use as input", ShortForm = "a")]
        public List<string> assemblies = null;

        [OptionDescription("Break into debugger", ShortForm = "break")]
        public bool breakIntoDebugger = false;

        [OptionDescription("Emit a 'capture state' directive after each statement, (default: false)", ShortForm = "c")]
        public bool captureState = false;

        [OptionDescription("Model exceptional control flow, (0: none, 1: explicit exceptions, 2: conservatively, default: 2)", ShortForm = "e")]
        public int modelExceptions = 2;

        [OptionDescription("Model type information, (0: minimal, 1: Include Subtyping, default: 0)", ShortForm = "typeInfo")]
        public int typeInfo = 0;

        [OptionDescription("Translation should be done for Get Me Here functionality, (default: false)", ShortForm = "gmh")]
        public bool getMeHere = false;

        [OptionDescription("Search paths for assembly dependencies.", ShortForm = "lib")]
        public List<string> libpaths = new List<string>();

        public enum HeapRepresentation { splitFields, twoDInt, twoDBox, general }
        [OptionDescription("Heap representation to use", ShortForm = "heap")]
        public HeapRepresentation heapRepresentation = HeapRepresentation.general;

        [OptionDescription("Translate using whole-program assumptions", ShortForm = "whole")]
        public bool wholeProgram = false;

        [OptionDescription("Stub assembly", ShortForm = "s")]
        public List<string>/*?*/ stub = null;

        [OptionDescription("Phone translation controls configuration")]
        public string phoneControls = null;

        [OptionDescription("Add phone navigation code on translation. Requires /phoneControls. Default false", ShortForm = "wpnav")]
        public bool phoneNavigationCode = false;

        [OptionDescription("Add phone feedback code on translation. Requires /phoneControls. Default false", ShortForm = "wpfb")]
        public bool phoneFeedbackCode = false;

        [OptionDescription("File containing white/black list (optionally end file name with + for white list, - for black list, default is white list", ShortForm = "exempt")]
        public string exemptionFile = "";

        [OptionDescription("Instrument branches with unique counter values", ShortForm = "ib")]
        public bool instrumentBranches = false;

        [OptionDescription("Add free ensures that express heap monotonicity", ShortForm = "heapM")]
        public bool monotonicHeap = false;

        public enum Dereference { Assert, Assume, None, }
        [OptionDescription("Assert/Assume on all object dereferences", ShortForm = "deref")]
        public Dereference dereference = Dereference.Assume;

    }
}