using System;
using System.Collections.Generic;
using Paramatrix.Content;

namespace Paramatrix.Scenario
{
    internal enum OpCode
    {
        /// <summary>Any non-control action: executed by the runtime according to its type.</summary>
        Action = 1,

        /// <summary>Evaluate condition; False → jump to target, True → next, Error → event fails.</summary>
        JumpIfFalse,

        Jump,

        End,
    }

    internal sealed class Instruction
    {
        public Instruction(OpCode op, ActionDefinition action, ConditionNode condition, int target)
        {
            Op = op;
            Action = action;
            Condition = condition;
            Target = target;
        }

        public OpCode Op { get; }

        public ActionDefinition Action { get; }

        public ConditionNode Condition { get; }

        public int Target { get; set; }
    }

    /// <summary>
    /// Flattens the nested action tree into instructions with jumps, so the runner is a plain program counter.
    /// Running past the last instruction is the implicit successful END (A8).
    /// </summary>
    internal static class ProgramCompiler
    {
        public static Instruction[] Compile(IReadOnlyList<ActionDefinition> actions)
        {
            var program = new List<Instruction>();
            Emit(actions, program);
            return program.ToArray();
        }

        private static void Emit(IReadOnlyList<ActionDefinition> actions, List<Instruction> program)
        {
            foreach (var action in actions)
            {
                switch (action)
                {
                    case IfAction branch:
                        var test = new Instruction(OpCode.JumpIfFalse, branch, branch.Condition, -1);
                        program.Add(test);
                        Emit(branch.Then, program);
                        if (branch.Else.Count == 0)
                        {
                            test.Target = program.Count;
                        }
                        else
                        {
                            var skipElse = new Instruction(OpCode.Jump, branch, null, -1);
                            program.Add(skipElse);
                            test.Target = program.Count;
                            Emit(branch.Else, program);
                            skipElse.Target = program.Count;
                        }

                        break;

                    case EndAction end:
                        program.Add(new Instruction(OpCode.End, end, null, -1));
                        break;

                    case null:
                        throw new ArgumentException("Action list contains null.");

                    default:
                        program.Add(new Instruction(OpCode.Action, action, null, -1));
                        break;
                }
            }
        }
    }
}
