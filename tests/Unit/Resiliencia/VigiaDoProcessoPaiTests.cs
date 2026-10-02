using System.Diagnostics;
using Edge.Worker.Resiliencia;

namespace Unit.Tests.Resiliencia;

/// <summary>
/// O worker morre com o serviço (docs/29, defeito de 01/10): sem o pai, ele encerra — limpo
/// primeiro, à força se a DLL o prender.
/// </summary>
public sealed class VigiaDoProcessoPaiTests
{
    private sealed class Cenario
    {
        public Cenario()
        {
            Vigia = new VigiaDoProcessoPai(
                () => PaiVivo,
                () => Limpas++,
                () => Forcadas++,
                tolerancia: TimeSpan.FromSeconds(10),
                relogio: () => Agora);
        }

        public bool PaiVivo { get; set; } = true;

        public int Limpas { get; private set; }

        public int Forcadas { get; private set; }

        public DateTimeOffset Agora { get; set; } = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

        public VigiaDoProcessoPai Vigia { get; }
    }

    [Fact]
    public void Com_o_pai_vivo_nada_acontece()
    {
        var c = new Cenario();

        for (var i = 0; i < 100; i++)
        {
            Assert.True(c.Vigia.Conferir());
            c.Agora += TimeSpan.FromSeconds(2);
        }

        Assert.Equal((0, 0), (c.Limpas, c.Forcadas));
        Assert.False(c.Vigia.PaiSumiu);
    }

    [Fact]
    public void Sem_o_pai_pede_a_parada_limpa_uma_vez_so()
    {
        var c = new Cenario();
        c.Vigia.Conferir();

        c.PaiVivo = false;
        Assert.False(c.Vigia.Conferir());
        Assert.False(c.Vigia.Conferir());

        // O pai "voltar" (PID reaproveitado) não desfaz: quem subiu o worker morreu.
        c.PaiVivo = true;
        Assert.False(c.Vigia.Conferir());

        Assert.Equal((1, 0), (c.Limpas, c.Forcadas));
        Assert.True(c.Vigia.PaiSumiu);
    }

    [Fact]
    public void Parada_limpa_que_nao_termina_na_tolerancia_vira_saida_forcada_uma_vez_so()
    {
        var c = new Cenario { PaiVivo = false };
        c.Vigia.Conferir();

        c.Agora += TimeSpan.FromSeconds(9);
        c.Vigia.Conferir();
        Assert.Equal(0, c.Forcadas);

        c.Agora += TimeSpan.FromSeconds(1);
        c.Vigia.Conferir();
        c.Agora += TimeSpan.FromSeconds(30);
        c.Vigia.Conferir();

        Assert.Equal((1, 1), (c.Limpas, c.Forcadas));
    }

    [Fact]
    public void Iniciada_a_vigia_confere_sozinha()
    {
        var vivo = true;
        using var encerrou = new ManualResetEventSlim();
        using var vigia = new VigiaDoProcessoPai(() => vivo, encerrou.Set, () => { }, intervalo: TimeSpan.FromMilliseconds(20));

        vigia.Iniciar();
        Assert.False(encerrou.Wait(TimeSpan.FromMilliseconds(100)));

        vivo = false;
        Assert.True(encerrou.Wait(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void Pai_por_pid_ve_o_proprio_processo_vivo_e_um_pid_que_nao_existe_morto()
    {
        Assert.True(VigiaDoProcessoPai.PaiPorPid(Environment.ProcessId)());
        Assert.False(VigiaDoProcessoPai.PaiPorPid(int.MaxValue - 7)());
    }

    /// <summary>Um processo de verdade que morre: a vigia percebe.</summary>
    [Fact]
    public void Pai_por_pid_percebe_o_processo_que_morreu()
    {
        using var pai = Process.Start(new ProcessStartInfo(Processo(), Argumentos()) { UseShellExecute = false, CreateNoWindow = true })!;

        try
        {
            var vivo = VigiaDoProcessoPai.PaiPorPid(pai.Id);
            Assert.True(vivo());

            pai.Kill();
            pai.WaitForExit();

            Assert.False(vivo());
        }
        finally
        {
            if (!pai.HasExited)
            {
                pai.Kill();
            }
        }
    }

    // Um processo que fica de pé até ser morto, presente em qualquer máquina.
    private static string Processo() => OperatingSystem.IsWindows() ? "cmd.exe" : "sleep";

    private static string Argumentos() => OperatingSystem.IsWindows() ? "/c pause" : "300";
}
