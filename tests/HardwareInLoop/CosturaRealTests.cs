using System.Reflection;
using System.Runtime.InteropServices;
using Topdata.EasyInner.Interop;

namespace HardwareInLoop.Tests;

/// <summary>
/// Achado E9-2 do docs/41: toda a suíte HIL roda contra a costura falsa, e a <see cref="EasyInnerReal"/>
/// (que liga o produto à DLL de verdade) não tinha teste. Um fio trocado ali, como
/// <c>LiberarCatracaEntrada</c> chamando a função de saída ou dois argumentos invertidos, passava
/// em todo o CI.
/// </summary>
/// <remarks>
/// Lê o IL de cada método da costura real e confere que ele só repassa: carrega os próprios
/// argumentos na ordem e chama exatamente uma função nativa com o mesmo nome, com os mesmos tipos de
/// parâmetro e retorno, declarada como P/Invoke da EasyInner com a convenção do manual. Não prova o
/// comportamento da DLL (isso é a bancada, HIL-STACK-01); prova que o fio vai para o lugar certo.
/// </remarks>
public sealed class CosturaRealTests
{
    private const string Dll = "EasyInner.dll";

    // Não falam com a DLL: buffer e leitura do cartão são código nosso, testado em AdapterTests.
    private static readonly HashSet<string> SemChamadaNativa = ["NovoBufferDeCartao", "LerCartao"];

    // Onde moram as declarações P/Invoke: a nossa e a gerada do SDK.
    private static readonly string[] Declaracoes = ["EasyInnerNative", "EasyInnerGerada"];

    public static TheoryData<string> Metodos()
    {
        var dados = new TheoryData<string>();
        foreach (var metodo in typeof(IEasyInnerNative).GetMethods().Where(m => !SemChamadaNativa.Contains(m.Name)))
        {
            dados.Add(metodo.Name);
        }

        return dados;
    }

    [Theory]
    [MemberData(nameof(Metodos))]
    public void Cada_metodo_da_costura_real_repassa_para_a_funcao_nativa_de_mesmo_nome_na_ordem(string nome)
    {
        var metodo = typeof(EasyInnerReal).GetMethod(nome, BindingFlags.Public | BindingFlags.Instance)
            ?? throw new InvalidOperationException($"EasyInnerReal não implementa {nome}.");

        var (argumentos, chamadas) = LerCorpo(metodo);

        var chamada = Assert.Single(chamadas);
        Assert.Equal(nome, chamada.Name);
        Assert.True(chamada.IsStatic);
        Assert.Contains(chamada.DeclaringType!.Name, Declaracoes);

        // Mesmos parâmetros, na mesma ordem e com os mesmos tipos (ref incluído), e o mesmo retorno.
        Assert.Equal(
            metodo.GetParameters().Select(p => p.ParameterType),
            chamada.GetParameters().Select(p => p.ParameterType));
        Assert.Equal(metodo.ReturnType, chamada.ReturnType);

        // Os argumentos do método, um de cada, na ordem em que chegaram.
        Assert.Equal(Enumerable.Range(1, metodo.GetParameters().Length), argumentos);

        // E a função chamada é a da DLL, pelo nome exato, com a convenção do manual (stdcall).
        var importacao = chamada.GetCustomAttribute<DllImportAttribute>();
        Assert.NotNull(importacao);
        Assert.Equal(Dll, importacao.Value);
        Assert.Equal(CallingConvention.Winapi, importacao.CallingConvention);
        Assert.True(importacao.ExactSpelling);
        Assert.True(string.IsNullOrEmpty(importacao.EntryPoint) || importacao.EntryPoint == nome);
    }

    [Fact]
    public void A_interface_inteira_esta_coberta()
    {
        // Um método novo na interface entra no teste acima sozinho; aqui só se garante que o teste
        // não ficou vazio por engano.
        Assert.True(Metodos().Count >= 30, $"só {Metodos().Count} métodos encontrados");
    }

    /// <summary>Os argumentos carregados (1..N) e os métodos chamados, a partir do IL.</summary>
    private static (List<int> Argumentos, List<MethodInfo> Chamadas) LerCorpo(MethodInfo metodo)
    {
        var il = metodo.GetMethodBody()?.GetILAsByteArray()
            ?? throw new InvalidOperationException($"{metodo.Name} sem corpo.");
        var argumentos = new List<int>();
        var chamadas = new List<MethodInfo>();

        for (var i = 0; i < il.Length;)
        {
            var op = il[i++];
            switch (op)
            {
                case 0x00: // nop
                case 0x2A: // ret
                case 0x0A: // stloc.0 (Debug)
                case 0x06: // ldloc.0 (Debug)
                    break;
                case >= 0x02 and <= 0x05: // ldarg.0 .. ldarg.3
                    argumentos.Add(op - 0x02);
                    break;
                case 0x0E: // ldarg.s
                    argumentos.Add(il[i++]);
                    break;
                case 0x2B: // br.s (Debug)
                    i++;
                    break;
                case 0x28: // call
                    var token = BitConverter.ToInt32(il, i);
                    i += 4;
                    chamadas.Add((MethodInfo)metodo.Module.ResolveMethod(token)!);
                    break;
                default:
                    throw new InvalidOperationException(
                        $"{metodo.Name}: instrução 0x{op:X2} inesperada. A costura real só deve repassar; " +
                        "lógica aqui não é coberta pelos testes da costura falsa.");
            }
        }

        // ldarg.0 é o this da instância: não é argumento repassado.
        argumentos.RemoveAll(a => a == 0);
        return (argumentos, chamadas);
    }
}
