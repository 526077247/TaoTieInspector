using System;
using System.Collections.Generic;
using System.Reflection;

namespace TaoTie.Inspector.Editor
{
    /// <summary>
    /// Evaluates Odin-style @ expressions: "@!IsGlobal", "@A && !B", "@(A || B) && !C"
    /// Supports: identifiers (bool fields/properties/methods), !, &amp;&amp;, ||, ==, !=, true, false, parentheses.
    /// </summary>
    public class TaoTieExpressionEvaluator
    {
        private enum TokenType
        {
            Identifier,
            Not,
            And,
            Or,
            Equal,
            NotEqual,
            LParen,
            RParen,
            True,
            False,
            EOF
        }

        private struct Token
        {
            public TokenType Type;
            public string Text;
        }

        private readonly string expression;
        private readonly List<Token> tokens;
        private int pos;

        private TaoTieExpressionEvaluator(string expression)
        {
            this.expression = expression;
            this.tokens = Tokenize(expression);
            this.pos = 0;
        }

        public static bool Evaluate(string expression, object target)
        {
            if (string.IsNullOrEmpty(expression)) return true;

            var expr = expression;
            if (expr[0] == '@')
                expr = expr.Substring(1);

            var evaluator = new TaoTieExpressionEvaluator(expr);
            return evaluator.Evaluate(target);
        }

        public static bool IsExpression(string member)
        {
            return !string.IsNullOrEmpty(member) && member[0] == '@';
        }

        #region Tokenizer

        private static List<Token> Tokenize(string expr)
        {
            var result = new List<Token>();
            int i = 0;

            while (i < expr.Length)
            {
                char c = expr[i];

                if (char.IsWhiteSpace(c))
                {
                    i++;
                    continue;
                }

                if (c == '!')
                {
                    if (i + 1 < expr.Length && expr[i + 1] == '=')
                    {
                        result.Add(new Token { Type = TokenType.NotEqual, Text = "!=" });
                        i += 2;
                    }
                    else
                    {
                        result.Add(new Token { Type = TokenType.Not, Text = "!" });
                        i++;
                    }
                    continue;
                }

                if (c == '&' && i + 1 < expr.Length && expr[i + 1] == '&')
                {
                    result.Add(new Token { Type = TokenType.And, Text = "&&" });
                    i += 2;
                    continue;
                }

                if (c == '|' && i + 1 < expr.Length && expr[i + 1] == '|')
                {
                    result.Add(new Token { Type = TokenType.Or, Text = "||" });
                    i += 2;
                    continue;
                }

                if (c == '=' && i + 1 < expr.Length && expr[i + 1] == '=')
                {
                    result.Add(new Token { Type = TokenType.Equal, Text = "==" });
                    i += 2;
                    continue;
                }

                if (c == '(')
                {
                    result.Add(new Token { Type = TokenType.LParen, Text = "(" });
                    i++;
                    continue;
                }

                if (c == ')')
                {
                    result.Add(new Token { Type = TokenType.RParen, Text = ")" });
                    i++;
                    continue;
                }

                // Identifier or keyword
                if (char.IsLetter(c) || c == '_')
                {
                    int start = i;
                    while (i < expr.Length && (char.IsLetterOrDigit(expr[i]) || expr[i] == '_'))
                        i++;
                    string word = expr.Substring(start, i - start);

                    if (string.Equals(word, "true", StringComparison.OrdinalIgnoreCase))
                        result.Add(new Token { Type = TokenType.True, Text = word });
                    else if (string.Equals(word, "false", StringComparison.OrdinalIgnoreCase))
                        result.Add(new Token { Type = TokenType.False, Text = word });
                    else
                        result.Add(new Token { Type = TokenType.Identifier, Text = word });
                    continue;
                }

                // Unknown char — skip to avoid infinite loop
                i++;
            }

            result.Add(new Token { Type = TokenType.EOF, Text = "" });
            return result;
        }

        #endregion

        #region Parser (recursive descent)

        // Grammar (lowest to highest precedence):
        //   or_expr   := and_expr ( '||' and_expr )*
        //   and_expr  := eq_expr ( '&&' eq_expr )*
        //   eq_expr   := not_expr ( '==' | '!=' not_expr )*
        //   not_expr  := '!' not_expr | primary
        //   primary   := identifier | 'true' | 'false' | '(' or_expr ')'

        private bool Evaluate(object target)
        {
            bool result = ParseOr(target);
            return result;
        }

        private bool ParseOr(object target)
        {
            bool left = ParseAnd(target);
            while (Current.Type == TokenType.Or)
            {
                Advance();
                bool right = ParseAnd(target);
                left = left || right;
            }
            return left;
        }

        private bool ParseAnd(object target)
        {
            bool left = ParseEquality(target);
            while (Current.Type == TokenType.And)
            {
                Advance();
                bool right = ParseEquality(target);
                left = left && right;
            }
            return left;
        }

        private bool ParseEquality(object target)
        {
            object left = ParseNot(target);
            while (Current.Type == TokenType.Equal || Current.Type == TokenType.NotEqual)
            {
                TokenType op = Current.Type;
                Advance();
                object right = ParseNot(target);

                bool equal = AreEqual(left, right);
                left = op == TokenType.Equal ? equal : !equal;
            }
            return ToBool(left);
        }

        private object ParseNot(object target)
        {
            if (Current.Type == TokenType.Not)
            {
                Advance();
                object val = ParseNot(target);
                return !ToBool(val);
            }
            return ParsePrimary(target);
        }

        private object ParsePrimary(object target)
        {
            Token t = Current;

            switch (t.Type)
            {
                case TokenType.True:
                    Advance();
                    return true;
                case TokenType.False:
                    Advance();
                    return false;
                case TokenType.LParen:
                    Advance();
                    bool val = ParseOr(target);
                    if (Current.Type == TokenType.RParen)
                        Advance();
                    return val;
                case TokenType.Identifier:
                    Advance();
                    return ResolveIdentifier(t.Text, target);
                default:
                    // Unexpected token — treat as true to avoid hiding fields
                    Advance();
                    return true;
            }
        }

        #endregion

        #region Value resolution

        private static object ResolveIdentifier(string name, object target)
        {
            if (target == null) return false;

            MemberInfo member = TaoTieConditionResolver.GetMember(target, name);
            if (member == null) return false;

            return member switch
            {
                FieldInfo fi => fi.GetValue(target),
                PropertyInfo pi => pi.GetValue(target, null),
                MethodInfo mi => mi.ReturnType == typeof(bool) && mi.GetParameters().Length == 0
                    ? mi.Invoke(target, null)
                    : false,
                _ => false
            };
        }

        private static bool AreEqual(object a, object b)
        {
            if (a == null && b == null) return true;
            if (a == null || b == null) return false;

            if (a is bool ba && b is bool bb) return ba == bb;
            if (a is Enum && b is Enum)
                return Convert.ToInt64(a) == Convert.ToInt64(b);

            return a.Equals(b);
        }

        private static bool ToBool(object value)
        {
            if (value is bool b) return b;
            if (value == null) return false;
            try
            {
                return Convert.ToBoolean(value);
            }
            catch
            {
                return value != null;
            }
        }

        #endregion

        #region Utilities

        private Token Current => tokens[pos];

        private void Advance()
        {
            if (pos < tokens.Count - 1)
                pos++;
        }

        #endregion
    }
}
