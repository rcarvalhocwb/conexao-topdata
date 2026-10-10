using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Contracts.Edge.V1;
using Desktop.ViewModels;

namespace Desktop.App;

internal static partial class CapturaDeTela
{
    /// <summary>Somente evidência opt-in do CI, contra o serviço real em simulação.</summary>
    private static async Task<Captura> GravarLgpdAsync(string caminho, JanelaViewModel vm,
        PessoasViewModel tela, EdgeControl.EdgeControlClient cliente)
    {
        if (!(await cliente.ObterEstadoAsync(new ObterEstadoRequest())).Simulacao)
            throw new InvalidOperationException("A evidência LGPD só pode criar dados fictícios em simulação.");
        var r = await cliente.GravarPessoaAsync(new GravarPessoaRequest
        {
            Pessoa = new PessoaDoCadastro { PerfilId = "aluno", NomeCompleto = "Titular de demonstração — dados fictícios", Telefone = "11 90000-0000" },
        });
        if (!r.Gravado) throw new InvalidOperationException("Não foi possível cadastrar a ficha fictícia da evidência LGPD.");
        try
        {
            await tela.AtualizarAsync().ConfigureAwait(true);
            await tela.AbrirAsync(r.Id).ConfigureAwait(true);
            if (!tela.PodeExportarDados || !tela.PodeExcluirTitular)
                throw new InvalidOperationException("Os botões LGPD não estão disponíveis para o administrador da captura.");
            var janela = new JanelaPrincipal(vm);
            try
            {
                var raiz = (FrameworkElement)janela.Content;
                janela.Content = null;
                var moldura = Moldura(raiz, vm);
                moldura.Measure(new Size(Largura, Altura));
                moldura.Arrange(new Rect(0, 0, Largura, Altura));
                moldura.UpdateLayout();
                await moldura.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
                var situacao = LgpdDescendentes<Border>(moldura).Single(b => b.Name == "SituacaoDaPessoa");
                DependencyObject? pai = VisualTreeHelper.GetParent(situacao);
                while (pai is not null && pai is not ScrollViewer) pai = VisualTreeHelper.GetParent(pai);
                if (pai is not ScrollViewer scroll) throw new InvalidOperationException("Seção Situação sem rolagem na captura.");
                var posicao = situacao.TransformToAncestor(scroll).Transform(new Point(0, 0));
                scroll.ScrollToVerticalOffset(scroll.VerticalOffset + posicao.Y);
                return await FotografarAsync(moldura, caminho).ConfigureAwait(true);
            }
            finally { janela.Close(); }
        }
        finally
        {
            await cliente.ExcluirTitularAsync(new ExcluirTitularRequest { PessoaId = r.Id, Confirmada = true });
        }
    }

    private static IEnumerable<T> LgpdDescendentes<T>(DependencyObject raiz) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(raiz); i++)
        {
            var filho = VisualTreeHelper.GetChild(raiz, i);
            if (filho is T item) yield return item;
            foreach (var descendente in LgpdDescendentes<T>(filho)) yield return descendente;
        }
    }
}
