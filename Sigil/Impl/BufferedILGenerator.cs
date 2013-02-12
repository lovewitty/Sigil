﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Sigil.Impl
{
    internal class BufferedILGenerator
    {
        public delegate void UpdateOpCodeDelegate(OpCode newOpcode);
        public delegate System.Reflection.Emit.Label DefineLabelDelegate(ILGenerator il);
        public delegate LocalBuilder DeclareLocallDelegate(ILGenerator il);

        public int Index { get { return Buffer.Count; } }

        private List<Action<ILGenerator, bool, StringBuilder>> Buffer = new List<Action<ILGenerator, bool, StringBuilder>>();
        private List<Func<int>> InstructionSizes = new List<Func<int>>();

        private Type DelegateType;

        public BufferedILGenerator(Type delegateType)
        {
            DelegateType = delegateType;
        }

        public string UnBuffer(ILGenerator il)
        {
            var log = new StringBuilder();

            for(var i = 0; i < Buffer.Count; i++)
            {
                var x = Buffer[i];

                x(il, false, log);
            }

            return log.ToString();
        }

        private int LengthTo(int end, out string instructions)
        {
            var instrs = new StringBuilder();

            foreach (var x in Buffer.Take(end))
            {
                x(null, true, instrs);
            }

            instructions = instrs.ToString();

            var instrSizes = InstructionSizes.Take(end).Select(s => s()).Sum();

            return instrSizes;
        }

        internal string[] Instructions(Dictionary<int, Local> locals)
        {
            var ret = new List<string>();

            var invoke = DelegateType.GetMethod("Invoke");
            var returnType = invoke.ReturnType;
            var parameterTypes = invoke.GetParameters().Select(s => s.ParameterType).ToArray();

            var dynMethod = new DynamicMethod(Guid.NewGuid().ToString(), returnType, parameterTypes);
            var il = dynMethod.GetILGenerator();

            var instrs = new StringBuilder();

            foreach (var x in Buffer)
            {
                x(il, true, instrs);
                var line = instrs.ToString().TrimEnd();

                if (line.StartsWith(OpCodes.Ldloc_0.ToString()) ||
                    line.StartsWith(OpCodes.Stloc_0.ToString()))
                {
                    line += " // " + locals[0];
                }

                if (line.StartsWith(OpCodes.Ldloc_1.ToString()) ||
                    line.StartsWith(OpCodes.Stloc_1.ToString()))
                {
                    line += " // " + locals[1];
                }

                if (line.StartsWith(OpCodes.Ldloc_2.ToString()) ||
                    line.StartsWith(OpCodes.Stloc_2.ToString()))
                {
                    line += " // " + locals[2];
                }

                if (line.StartsWith(OpCodes.Ldloc_3.ToString()) ||
                    line.StartsWith(OpCodes.Stloc_3.ToString()))
                {
                    line += " // " + locals[3];
                }

                if (line.StartsWith(OpCodes.Ldloc_S.ToString()) ||
                    line.StartsWith(OpCodes.Stloc_S.ToString()))
                {
                    line += " // " + ExtractLocal(line, locals);
                }

                ret.Add(line);
                instrs.Clear();
            }

            return ret.ToArray();
        }

        private static Regex _ExtractLocal = new Regex(@"\s+(?<locId>\d+)", RegexOptions.Compiled);

        private static Local ExtractLocal(string from, Dictionary<int, Local> locals)
        {
            var match = _ExtractLocal.Match(from);

            var locId = match.Groups["locId"].Value;

            var lid = int.Parse(locId);

            return locals[lid];
        }

        public int ByteDistance(int start, int stop)
        {
            string ignored;
            var toStart = LengthTo(start, out ignored);
            var toStop = LengthTo(stop, out ignored);

            return toStop - toStart;
        }

        public void Insert(int ix, OpCode op)
        {
            if (ix < 0 || ix > Buffer.Count)
            {
                throw new ArgumentOutOfRangeException("ix", "Expected value between 0 and " + Buffer.Count);
            }

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
        }

        public void Emit(OpCode op)
        {
            InstructionSizes.Add(() => InstructionSize.Get(op));

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
        }

        public void Emit(OpCode op, byte b)
        {
            InstructionSizes.Add(() => InstructionSize.Get(op));

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
        }

        public void Emit(OpCode op, short s)
        {
            InstructionSizes.Add(() => InstructionSize.Get(op));

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
        }

        public void Emit(OpCode op, int i)
        {
            InstructionSizes.Add(() => InstructionSize.Get(op));

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
        }

        public void Emit(OpCode op, uint ui)
        {
            int asInt;
            unchecked
            {
                asInt = (int)ui;
            }

            InstructionSizes.Add(() => InstructionSize.Get(op));

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
        }

        public void Emit(OpCode op, long l)
        {
            InstructionSizes.Add(() => InstructionSize.Get(op));

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
        }

        public void Emit(OpCode op, ulong ul)
        {
            long asLong;
            unchecked
            {
                asLong = (long)ul; 
            }

            InstructionSizes.Add(() => InstructionSize.Get(op));

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
        }

        public void Emit(OpCode op, float f)
        {
            InstructionSizes.Add(() => InstructionSize.Get(op));

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
        }

        public void Emit(OpCode op, double d)
        {
            InstructionSizes.Add(() => InstructionSize.Get(op));

            Buffer.Add(
                (il, logOnly, log) => 
                {
                    if (!logOnly)
                    {
                        il.Emit(op, d);
                    }
                    
                    log.AppendLine(op + " " + d);
                });
        }

        public void Emit(OpCode op, MethodInfo method)
        {
            InstructionSizes.Add(() => InstructionSize.Get(op));

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
        }

        public void Emit(OpCode op, ConstructorInfo cons)
        {
            InstructionSizes.Add(() => InstructionSize.Get(op));

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
        }

        public void Emit(OpCode op, Type type)
        {
            InstructionSizes.Add(() => InstructionSize.Get(op));

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
        }

        public void Emit(OpCode op, FieldInfo field)
        {
            InstructionSizes.Add(() => InstructionSize.Get(op));

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
        }

        public void Emit(OpCode op, string str)
        {
            InstructionSizes.Add(() => InstructionSize.Get(op));

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
        }

        public void Emit(OpCode op, Sigil.Label label, out UpdateOpCodeDelegate update)
        {
            var localOp = op;

            update =
                newOpcode =>
                {
                    localOp = newOpcode;
                };

            InstructionSizes.Add(() => InstructionSize.Get(localOp));

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
        }

        public void Emit(OpCode op, Sigil.Label[] labels, out UpdateOpCodeDelegate update)
        {
            var localOp = op;

            update =
                newOpcode =>
                {
                    localOp = newOpcode;
                };

            InstructionSizes.Add(() => InstructionSize.Get(localOp));

            Buffer.Add(
                (il, logOnly, log) =>
                {
                    if (!logOnly)
                    {
                        var ls = labels.Select(l => l.LabelDel(il)).ToArray();
                        il.Emit(localOp, ls);
                    }

                    log.AppendLine(localOp + " " + string.Join(", ", labels.AsEnumerable()));
                }
            );
        }

        public void Emit(OpCode op, Sigil.Local local)
        {
            InstructionSizes.Add(() => InstructionSize.Get(op));

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
        }

        public void Emit(OpCode op, CallingConventions callConventions, Type returnType, Type[] parameterTypes)
        {
            InstructionSizes.Add(() => InstructionSize.Get(op));

            Buffer.Add(
                (il, logOnly, log) =>
                {
                    if (!logOnly)
                    {
                        il.EmitCalli(op, callConventions, returnType, parameterTypes, null);
                    }

                    log.AppendLine(op + " " + callConventions + " " + returnType + " " + string.Join(" ", (IEnumerable<Type>)parameterTypes));
                }
            );
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

            return ret;
        }

        public void BeginCatchBlock(Type exception)
        {
            InstructionSizes.Add(() => InstructionSize.BeginCatchBlock());

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
        }

        public void EndExceptionBlock()
        {
            InstructionSizes.Add(() => InstructionSize.EndExceptionBlock());

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
        }

        public void EndCatchBlock()
        {
            InstructionSizes.Add(() => InstructionSize.EndCatchBlock());

            Buffer.Add(
                (il, logOnly, log) =>
                {
                    log.AppendLine("--EndCatchBlock--");
                }
            );
        }

        public void BeginFinallyBlock()
        {
            InstructionSizes.Add(() => InstructionSize.BeginFinallyBlock());

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
        }

        public void EndFinallyBlock()
        {
            InstructionSizes.Add(() => InstructionSize.EndFinallyBlock());

            Buffer.Add(
                (il, logOnly, log) =>
                {
                    log.AppendLine("--EndFinallyBlock--");
                }
            );
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

            Buffer.Add(
                (il, logOnly, log) => 
                {
                    if (!logOnly)
                    {
                        ret(il);
                    }
                }
            );

            return ret;
        }

        public void MarkLabel(Sigil.Label label)
        {
            InstructionSizes.Add(() => InstructionSize.MarkLabel());

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

            Buffer.Add(
                (il, logOnly,log) => 
                {
                    if (!logOnly)
                    {
                        ret(il);
                    }
                }
            );

            return ret;
        }
    }
}
