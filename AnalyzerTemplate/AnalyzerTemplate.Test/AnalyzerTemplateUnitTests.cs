using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VerifyCS = AnalyzerTemplate.Test.CSharpCodeFixVerifier<
    AnalyzerTemplate.AnalyzerTemplateAnalyzer,
    AnalyzerTemplate.AnalyzerTemplateCodeFixProvider>;

namespace AnalyzerTemplate.Test
{
    [TestClass]
    public class AnalyzerTemplateUnitTest
    {
        //No diagnostics expected to show up

        //Diagnostic and CodeFix both triggered and checked for
        [TestMethod]
        public async Task MagicNumberInLocalVariableTest()
        {
            var test = @"
using System;

namespace AnalyzerTemplate
{
    public class MainClass
    {
        private const int _magicNumber0 = 10;

        static void Main()
        {
            int a = 5;
        }
    }
}
";

            var fixtest = @"
using System;

namespace AnalyzerTemplate
{
    public class MainClass
    {
        private const int _magicNumber0 = 10;
        private const int _magicNumber1 = 5;

        static void Main()
        {
            int a = _magicNumber1;
        }
    }
}
";

            var expected = new DiagnosticResult("MagicNumbersDiagnostic", DiagnosticSeverity.Warning).WithSpan(12, 21, 12, 22);
            await VerifyCS.VerifyCodeFixAsync(test, expected, fixtest);
        }

        [TestMethod]
        public async Task MagicNumberInIfTest()
        {
            var test = @"
using System;

namespace AnalyzerTemplate
{
    public class MainClass
    {
        private const int x = 10;
        static void Main()
        {
            if (x == 10)
                Console.WriteLine();
        }
    }
}
";

            var fixtest = @"
using System;

namespace AnalyzerTemplate
{
    public class MainClass
    {
        private const int x = 10;
        private const int _magicNumber0 = 10;

        static void Main()
        {
            if (x == _magicNumber0)
                Console.WriteLine();
        }
    }
}
";

            var expected = new DiagnosticResult("MagicNumbersDiagnostic", DiagnosticSeverity.Warning).WithSpan(11, 22, 11, 24);
            await VerifyCS.VerifyCodeFixAsync(test, expected, fixtest);
        }

        [TestMethod]
        public async Task MagicNumberAsMethodParameterTest()
        {
            var test = @"
using System;

namespace AnalyzerTemplate
{
    public class MainClass
    {
        public static int square(int x)
        {
            return x * x;
        }

        static void Main()
        {
            Console.WriteLine(square(10));
        }
    }
}
";

            var fixtest = @"
using System;

namespace AnalyzerTemplate
{
    public class MainClass
    {
        private const int _magicNumber0 = 10;

        public static int square(int x)
        {
            return x * x;
        }

        static void Main()
        {
            Console.WriteLine(square(_magicNumber0));
        }
    }
}
";

            var expected = new DiagnosticResult("MagicNumbersDiagnostic", DiagnosticSeverity.Warning).WithSpan(15, 38, 15, 40);
            await VerifyCS.VerifyCodeFixAsync(test, expected, fixtest);
        }

        [TestMethod]
        public async Task BackingFieldWithGetonlyPropertyTest()
        {
            var test = @"
using System;

namespace AnalyzerTemplate
{
    public class MainClass
    {
        private const int x = 10;
        public int X => x;
        static void Main()
        {
        }
    }
}
";

            var fixtest = @"
using System;

namespace AnalyzerTemplate
{
    public class MainClass
    {
        public int X { get; private set; }
        static void Main()
        {
        }
    }
}
";

            var expected = new DiagnosticResult("UselessBackingFieldsDiagnostic", DiagnosticSeverity.Warning).WithSpan(8, 27, 8, 28).WithSpan(9, 9, 9, 27);
            await VerifyCS.VerifyCodeFixAsync(test, expected, fixtest);
        }

        [TestMethod]
        public async Task BackingFieldWithPropertyTest()
        {
            var test = @"
using System;

namespace AnalyzerTemplate
{
    public class MainClass
    {
        private int x = 10;
        public int X { get => x; set => x = value; }
        static void Main()
        {
            
        }
    }
}
";

            var fixtest = @"
using System;

namespace AnalyzerTemplate
{
    public class MainClass
    {
        private int x = 10;
        public int X { get => x; set => x = value; }
        static void Main()
        {
            
        }
    }
}
";
            await VerifyCS.VerifyCodeFixAsync(test, fixtest);
        }
    }
}
