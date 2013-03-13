﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using System.Text.RegularExpressions;

namespace Sigil.Impl
{
    internal class BufferedILInstruction
    {
        public bool DefinesLabel { get; internal set; }
        public Sigil.Label MarksLabel { get; internal set; }

        public bool StartsExceptionBlock { get; internal set; }
        public bool EndsExceptionBlock { get; internal set; }
        
        public bool StartsCatchBlock { get; internal set; }
        public bool EndsCatchBlock { get; internal set; }
        
        public bool StartsFinallyBlock { get; internal set; }
        public bool EndsFinallyBlock { get; internal set; }
        
        public bool DeclaresLocal { get; internal set; }

        public OpCode? IsInstruction { get; internal set; }

        public Type MethodReturnType { get; internal set; }
        public IEnumerable<Type> MethodParameterTypes { get; internal set; }
    }

    internal class BufferedILGenerator
    {
        public BufferedILInstruction this[int ix]
        {
            get
            {
                return TraversableBuffer[ix];
            }
        }

        public int Index { get { return Buffer.Count; } }

        private List<Action<ILGenerator, bool, StringBuilder>> Buffer = new List<Action<ILGenerator, bool, StringBuilder>>();
        private List<BufferedILInstruction> TraversableBuffer = new List<BufferedILInstruction>();
        private List<Func<int>> InstructionSizes = new List<Func<int>>();

        private Type DelegateType;

        public BufferedILGenerator(Type delegateType)
        {
            DelegateType = delegateType;
        }

        public string UnBuffer(ILGenerator il)
        {
            var log = new StringBuilder();

            // First thing will always be a Mark for tracing purposes; no reason to actually do it
            for(var i = 2; i < Buffer.Count; i++)
            {
                var x = Buffer[i];

                x(il, false, log);
            }

            return log.ToString();
        }

        private Dictionary<int, int> LengthCache = new Dictionary<int, int>();

        private int LengthTo(int end)
        {
            if (end == 0)
            {
                return 0;
            }

            int cached;
            if (LengthCache.TryGetValue(end, out cached))
            {
                return cached;
            }

            int runningTotal = 0;

            for (var i = 0; i < end; i++)
            {
                var s = InstructionSizes[i];

                runningTotal += s();

                LengthCache[i + 1] = runningTotal;
            }

            cached = LengthCache[end];

            return cached;
        }

        internal string[] Instructions(List<Local> locals)
        {
            var ret = new List<string>();

            var invoke = DelegateType.GetMethod("Invoke");
            var returnType = invoke.ReturnType;
            var parameterTypes = invoke.GetParameters().Select(s => s.ParameterType).ToArray();

            var dynMethod = new DynamicMethod(Guid.NewGuid().ToString(), returnType, parameterTypes);
            var il = dynMethod.GetILGenerator();

            var instrs = new StringBuilder();

            for(var i = 0; i < Buffer.Count; i++)
            {
                var x = Buffer[i];

                x(il, true, instrs);
                var line = instrs.ToString().TrimEnd();

                if (line.StartsWith(OpCodes.Ldloc_0.ToString()) ||
                    line.StartsWith(OpCodes.Stloc_0.ToString()))
                {
                    line += " // " + GetInScopeAt(locals, i)[0];
                }

                if (line.StartsWith(OpCodes.Ldloc_1.ToString()) ||
                    line.StartsWith(OpCodes.Stloc_1.ToString()))
                {
                    line += " // " + GetInScopeAt(locals, i)[1];
                }

                if (line.StartsWith(OpCodes.Ldloc_2.ToString()) ||
                    line.StartsWith(OpCodes.Stloc_2.ToString()))
                {
                    line += " // " + GetInScopeAt(locals, i)[2];
                }

                if (line.StartsWith(OpCodes.Ldloc_3.ToString()) ||
                    line.StartsWith(OpCodes.Stloc_3.ToString()))
                {
                    line += " // " + GetInScopeAt(locals, i)[3];
                }

                if (line.StartsWith(OpCodes.Ldloc_S.ToString()) ||
                    line.StartsWith(OpCodes.Stloc_S.ToString()))
                {
                    line += " // " + ExtractLocal(line, locals, i);
                }

                ret.Add(line);
                instrs.Length = 0;
            }

            return ret.ToArray();
        }

        private static Dictionary<int, Local> GetInScopeAt(List<Local> allLocals, int ix)
        {
            return
                allLocals
                    .Where(
                        l =>
                            l.DeclaredAtIndex <= ix &&
                            (l.ReleasedAtIndex == null || l.ReleasedAtIndex > ix)
                    ).ToDictionary(d => (int)d.Index, d => d);
        }

        private static Regex _ExtractLocal = new Regex(@"\s+(?<locId>\d+)", RegexOptions.Compiled);

        private static Local ExtractLocal(string from, List<Local> locals, int ix)
        {
            var match = _ExtractLocal.Match(from);

            var locId = match.Groups["locId"].Value;

            var lid = int.Parse(locId);

            return GetInScopeAt(locals, ix)[lid];
        }

        public int ByteDistance(int start, int stop)
        {
            var toStart = LengthTo(start);
            var toStop = LengthTo(stop);

            return toStop - toStart;
        }

        public void Remove(int ix)
        {
            if (ix < 0 || ix >= Buffer.Count)
            {
                throw new ArgumentOutOfRangeException("ix", "Expected value between 0 and " + Buffer.Count);
            }

            LengthCache.Clear();

            InstructionSizes.RemoveAt(ix);

            Buffer.RemoveAt(ix);

            TraversableBuffer.RemoveAt(ix);
        }

        public void Insert(int ix, OpCode op)
        {
            if (ix < 0 || ix > Buffer.Count)
            {
                throw new ArgumentOutOfRangeException("ix", "Expected value between 0 and " + Buffer.Count);
            }

            LengthCache.Clear();

            InstructionSizes.Insert(ix, () => InstructionSize.Get(op));

            Buffer.Insert(
                ix, 
                (il, logOnly, log) => 
                {
                    if (!logOnly)
                    {
                        il.Emit(op);
                    }

                    if (op.IsPrefix())
                    {
                        log.Append(op.ToString());
                    }
                    else
                    {
                        log.AppendLine(op.ToString());
                    }
                }
            );

            TraversableBuffer.Insert(
                ix,
                new BufferedILInstruction
                {
                    IsInstruction = op
                }
            );
        }

        public void Emit(OpCode op)
        {
            InstructionSizes.Add(() => InstructionSize.Get(op));

            LengthCache.Clear();

            Buffer.Add(
                (il, logOnly, log) => 
                {
                    if (!logOnly)
                    {
                        il.Emit(op);
                    }

                    if (op.IsPrefix())
                    {
                        log.Append(op.ToString());
                    }
                    else
                    {
                        log.AppendLine(op.ToString()); 
                    }
                }
            );

            TraversableBuffer.Add(new BufferedILInstruction { IsInstruction = op });
        }

        public void Emit(OpCode op, byte b)
        {
            InstructionSizes.Add(() => InstructionSize.Get(op));

            LengthCache.Clear();

            Buffer.Add(
                (il, logOnly, log) =>
                {
                    if (!logOnly)
                    {
                        il.Emit(op, b);
                    }

                    if (op.IsPrefix())
                    {
                        log.Append(op + "" + b + ".");
                    }
                    else
                    {
                        log.AppendLine(op + " " + b);
                    }
                }
            );

            TraversableBuffer.Add(new BufferedILInstruction { IsInstruction = op });
        }

        public void Emit(OpCode op, short s)
        {
            InstructionSizes.Add(() => InstructionSize.Get(op));

            LengthCache.Clear();

            Buffer.Add(
                (il, logOnly, log) =>
                {
                    if (!logOnly)
                    {
                        il.Emit(op, s);
                    }

                    if (op.IsPrefix())
                    {
                        log.Append(op + "" + s + ".");
                    }
                    else
                    {
                        log.AppendLine(op + " " + s);
                    }
                }
            );

            TraversableBuffer.Add(new BufferedILInstruction { IsInstruction = op });
        }

        public void Emit(OpCode op, int i)
        {
            InstructionSizes.Add(() => InstructionSize.Get(op));

            LengthCache.Clear();

            Buffer.Add(
                (il, logOnly, log) => 
                {
                    if (!logOnly)
                    {
                        il.Emit(op, i);
                    }

                    if (op.IsPrefix())
                    {
                        log.Append(op + "" + i + ".");
                    }
                    else
                    {
                        log.AppendLine(op + " " + i);
                    }
                }
            );

            TraversableBuffer.Add(new BufferedILInstruction { IsInstruction = op });
        }

        public void Emit(OpCode op, uint ui)
        {
            int asInt;
            unchecked
            {
                asInt = (int)ui;
            }

            InstructionSizes.Add(() => InstructionSize.Get(op));

            LengthCache.Clear();

            Buffer.Add(
                (il, logOnly, log) => 
                {
                    if (!logOnly)
                    {
                        il.Emit(op, asInt);
                    }
                    
                    log.AppendLine(op + " " + ui);
                }
            );

            TraversableBuffer.Add(new BufferedILInstruction { IsInstruction = op });
        }

        public void Emit(OpCode op, long l)
        {
            InstructionSizes.Add(() => InstructionSize.Get(op));

            LengthCache.Clear();

            Buffer.Add(
                (il, logOnly, log) => 
                {
                    if (!logOnly)
                    {
                        il.Emit(op, l);
                    }
                    
                    log.AppendLine(op + " " + l); 
                }
            );

            TraversableBuffer.Add(new BufferedILInstruction { IsInstruction = op });
        }

        public void Emit(OpCode op, ulong ul)
        {
            long asLong;
            unchecked
            {
                asLong = (long)ul; 
            }

            InstructionSizes.Add(() => InstructionSize.Get(op));

            LengthCache.Clear();

            Buffer.Add(
                (il, logOnly, log) => 
                {
                    if (!logOnly)
                    {
                        il.Emit(op, asLong);
                    }
                    
                    log.AppendLine(op + " " + ul); 
                }
            );

            TraversableBuffer.Add(new BufferedILInstruction { IsInstruction = op });
        }

        public void Emit(OpCode op, float f)
        {
            InstructionSizes.Add(() => InstructionSize.Get(op));

            LengthCache.Clear();

            Buffer.Add(
                (il, logOnly, log) => 
                {
                    if (!logOnly)
                    {
                        il.Emit(op, f);
                    }
                    
                    log.AppendLine(op + " " + f); 
                }
            );

            TraversableBuffer.Add(new BufferedILInstruction { IsInstruction = op });
        }

        public void Emit(OpCode op, double d)
        {
            InstructionSizes.Add(() => InstructionSize.Get(op));

            LengthCache.Clear();

            Buffer.Add(
                (il, logOnly, log) => 
                {
                    if (!logOnly)
                    {
                        il.Emit(op, d);
                    }
                    
                    log.AppendLine(op + " " + d);
                });

            TraversableBuffer.Add(new BufferedILInstruction { IsInstruction = op });
        }

        public void Emit(OpCode op, MethodInfo method)
        {
            InstructionSizes.Add(() => InstructionSize.Get(op));

            LengthCache.Clear();

            Buffer.Add(
                (il, logOnly, log) => 
                {
                    if (!logOnly)
                    {
                        il.Emit(op, method);
                    }

                    log.AppendLine(op + " " + method); 
                }
            );

            var parameters = method.GetParameters().Select(p => p.ParameterType).ToList();
            if(!method.IsStatic)
            {
                var declaring = method.DeclaringType;

                if (declaring.IsValueType)
                {
                    declaring = declaring.MakePointerType();
                }

                parameters.Insert(0, declaring);
            }

            TraversableBuffer.Add(new BufferedILInstruction { IsInstruction = op, MethodReturnType = method.ReturnType, MethodParameterTypes = parameters });
        }

        public void Emit(OpCode op, ConstructorInfo cons)
        {
            InstructionSizes.Add(() => InstructionSize.Get(op));

            LengthCache.Clear();

            Buffer.Add(
                (il, logOnly, log) => 
                {
                    if (!logOnly)
                    {
                        il.Emit(op, cons);
                    }
                    
                    log.AppendLine(op + " " + cons); 
                }
            );

            TraversableBuffer.Add(new BufferedILInstruction { IsInstruction = op });
        }

        public void Emit(OpCode op, Type type)
        {
            InstructionSizes.Add(() => InstructionSize.Get(op));

            LengthCache.Clear();

            Buffer.Add(
                (il, logOnly, log) => 
                {
                    if (!logOnly)
                    {
                        il.Emit(op, type);
                    }

                    log.AppendLine(op + " " + type); 
                }
            );

            TraversableBuffer.Add(new BufferedILInstruction { IsInstruction = op });
        }

        public void Emit(OpCode op, FieldInfo field)
        {
            InstructionSizes.Add(() => InstructionSize.Get(op));

            LengthCache.Clear();

            Buffer.Add(
                (il, logOnly, log) => 
                {
                    if (!logOnly)
                    {
                        il.Emit(op, field);
                    }

                    log.AppendLine(op + " " + field); 
                }
            );

            TraversableBuffer.Add(new BufferedILInstruction { IsInstruction = op });
        }

        public void Emit(OpCode op, string str)
        {
            InstructionSizes.Add(() => InstructionSize.Get(op));

            LengthCache.Clear();

            Buffer.Add(
                (il, logOnly, log) => 
                {
                    if (!logOnly)
                    {
                        il.Emit(op, str);
                    }
                    
                    log.AppendLine(op + " '" + str.Replace("'", @"\'") + "'");
                }
            );

            TraversableBuffer.Add(new BufferedILInstruction { IsInstruction = op });
        }

        public void Emit(OpCode op, Sigil.Label label, out UpdateOpCodeDelegate update)
        {
            var localOp = op;

            update =
                newOpcode =>
                {
                    LengthCache.Clear();

                    localOp = newOpcode;
                };

            InstructionSizes.Add(() => InstructionSize.Get(localOp));

            LengthCache.Clear();

            Buffer.Add(
                (il, logOnly, log) => 
                {
                    if (!logOnly)
                    {
                        var l = label.LabelDel(il);
                        il.Emit(localOp, l);
                    }

                    log.AppendLine(localOp + " " + label);
                }
            );

            TraversableBuffer.Add(new BufferedILInstruction { IsInstruction = op });
        }

        public void Emit(OpCode op, Sigil.Label[] labels, out UpdateOpCodeDelegate update)
        {
            var localOp = op;

            update =
                newOpcode =>
                {
                    LengthCache.Clear();

                    localOp = newOpcode;
                };

            InstructionSizes.Add(() => InstructionSize.Get(localOp));

            LengthCache.Clear();

            Buffer.Add(
                (il, logOnly, log) =>
                {
                    if (!logOnly)
                    {
                        var ls = labels.Select(l => l.LabelDel(il)).ToArray();
                        il.Emit(localOp, ls);
                    }

                    log.AppendLine(localOp + " " + Join(", ", labels.AsEnumerable()));
                }
            );

            TraversableBuffer.Add(new BufferedILInstruction { IsInstruction = op });
        }

        internal static string Join<T>(string delimiter, IEnumerable<T> parts) where T: class
        {
            using (var iter = parts.GetEnumerator())
            {
                if (!iter.MoveNext()) return "";
                var sb = new StringBuilder();
                var next = iter.Current;
                if (next != null) sb.Append(next);
                while (iter.MoveNext())
                {
                    sb.Append(delimiter);
                    next = iter.Current;
                    if (next != null) sb.Append(next);
                }
                return sb.ToString();
            }
        }
        public void Emit(OpCode op, Sigil.Local local)
        {
            InstructionSizes.Add(() => InstructionSize.Get(op));

            LengthCache.Clear();

            Buffer.Add(
                (il, logOnly, log) => 
                    {
                        if (!logOnly)
                        {
                            var l = local.LocalDel(il);
                            il.Emit(op, l);
                        }

                        log.AppendLine(op + " " + local);
                    }
            );

            TraversableBuffer.Add(new BufferedILInstruction { IsInstruction = op });
        }

        public void Emit(OpCode op, CallingConventions callConventions, Type returnType, Type[] parameterTypes)
        {
            InstructionSizes.Add(() => InstructionSize.Get(op));

            LengthCache.Clear();

            Buffer.Add(
                (il, logOnly, log) =>
                {
                    if (!logOnly)
                    {
                        il.EmitCalli(op, callConventions, returnType, parameterTypes, null);
                    }

                    log.AppendLine(op + " " + callConventions + " " + returnType + " " + Join(" ", (IEnumerable<Type>)parameterTypes));
                }
            );

            TraversableBuffer.Add(new BufferedILInstruction { IsInstruction = op, MethodReturnType = returnType, MethodParameterTypes = parameterTypes  });
        }

        public DefineLabelDelegate BeginExceptionBlock()
        {
            ILGenerator forIl = null;
            System.Reflection.Emit.Label? l = null;

            DefineLabelDelegate ret =
                il =>
                {
                    if (forIl != null && forIl != il)
                    {
                        l = null;
                    }

                    if (l != null) return l.Value;

                    forIl = il;
                    l = forIl.BeginExceptionBlock();

                    return l.Value;
                };

            InstructionSizes.Add(() => InstructionSize.BeginExceptionBlock());

            LengthCache.Clear();

            Buffer.Add(
                (il, logOnly, log) => 
                {
                    if (!logOnly)
                    {
                        ret(il);
                    }

                    log.AppendLine("--BeginExceptionBlock--");
                }
            );

            TraversableBuffer.Add(new BufferedILInstruction { StartsExceptionBlock = true });

            return ret;
        }

        public void BeginCatchBlock(Type exception)
        {
            InstructionSizes.Add(() => InstructionSize.BeginCatchBlock());

            LengthCache.Clear();

            Buffer.Add(
                (il, logOnly, log) =>
                {
                    if (!logOnly)
                    {
                        il.BeginCatchBlock(exception);
                    }

                    log.AppendLine("--BeginCatchBlock(" + exception + ")--");
                }
            );

            TraversableBuffer.Add(new BufferedILInstruction { StartsCatchBlock = true });
        }

        public void EndExceptionBlock()
        {
            InstructionSizes.Add(() => InstructionSize.EndExceptionBlock());

            LengthCache.Clear();

            Buffer.Add(
                (il, logOnly, log) =>
                {
                    if (!logOnly)
                    {
                        il.EndExceptionBlock();
                    }

                    log.AppendLine("--EndExceptionBlock--");
                }
            );

            TraversableBuffer.Add(new BufferedILInstruction { EndsExceptionBlock = true });
        }

        public void EndCatchBlock()
        {
            InstructionSizes.Add(() => InstructionSize.EndCatchBlock());

            LengthCache.Clear();

            Buffer.Add(
                (il, logOnly, log) =>
                {
                    log.AppendLine("--EndCatchBlock--");
                }
            );

            TraversableBuffer.Add(new BufferedILInstruction { EndsCatchBlock = true });
        }

        public void BeginFinallyBlock()
        {
            InstructionSizes.Add(() => InstructionSize.BeginFinallyBlock());

            LengthCache.Clear();

            Buffer.Add(
                (il, logOnly, log) =>
                {
                    if (!logOnly)
                    {
                        il.BeginFinallyBlock();
                    }

                    log.AppendLine("--BeginFinallyBlock--");
                }
            );

            TraversableBuffer.Add(new BufferedILInstruction { StartsFinallyBlock = true });
        }

        public void EndFinallyBlock()
        {
            InstructionSizes.Add(() => InstructionSize.EndFinallyBlock());

            LengthCache.Clear();

            Buffer.Add(
                (il, logOnly, log) =>
                {
                    log.AppendLine("--EndFinallyBlock--");
                }
            );

            TraversableBuffer.Add(new BufferedILInstruction { EndsFinallyBlock = true });
        }

        public DefineLabelDelegate DefineLabel()
        {
            ILGenerator forIl = null;
            System.Reflection.Emit.Label? l = null;

            DefineLabelDelegate ret =
                il =>
                {
                    if(forIl != null && forIl != il)
                    {
                        l = null;
                    }

                    if (l != null) return l.Value;
                    
                    forIl = il;
                    l = forIl.DefineLabel();

                    return l.Value;
                };

            InstructionSizes.Add(() => InstructionSize.DefineLabel());

            LengthCache.Clear();

            Buffer.Add(
                (il, logOnly, log) => 
                {
                    if (!logOnly)
                    {
                        ret(il);
                    }
                }
            );

            TraversableBuffer.Add(new BufferedILInstruction { DefinesLabel = true });

            return ret;
        }

        public void MarkLabel(Sigil.Label label)
        {
            InstructionSizes.Add(() => InstructionSize.MarkLabel());

            LengthCache.Clear();

            Buffer.Add(
                (il, logOnly, log) =>
                {
                    if (!logOnly)
                    {
                        var l = label.LabelDel(il);
                        il.MarkLabel(l);
                    }

                    log.AppendLine();
                    log.AppendLine(label + ":");
                }
            );

            TraversableBuffer.Add(new BufferedILInstruction { MarksLabel = label });
        }

        public DeclareLocallDelegate DeclareLocal(Type type)
        {
            ILGenerator forIl = null;
            LocalBuilder l = null;

            DeclareLocallDelegate ret =
                il =>
                {
                    if(forIl != null && il != forIl)
                    {
                        l = null;
                    }

                    if (l != null) return l;

                    forIl = il;
                    l = forIl.DeclareLocal(type);

                    return l;
                };

            InstructionSizes.Add(() => InstructionSize.DeclareLocal());

            LengthCache.Clear();

            Buffer.Add(
                (il, logOnly,log) => 
                {
                    if (!logOnly)
                    {
                        ret(il);
                    }
                }
            );

            TraversableBuffer.Add(new BufferedILInstruction { DeclaresLocal = true });

            return ret;
        }
    }
}
