using SteamSwitcher.Services.Updates;
using Xunit;

namespace SteamSwitcher.Tests;

public class ReleaseNotesFormatterTests
{
    [Fact]
    public void KeepsChangesButRemovesReleaseBoilerplate()
    {
        var notes = "## ✨ Sobre esta versão\nEsta versão do SteamNexus reúne as alterações listadas abaixo.\n### Correções\n- Corrigimos o alinhamento.\n### Novidades\n- Nova visualização compacta.\n**Full Changelog**: https://github.com/example/compare/v1...v2\n## 📥 Como instalar\n- Baixe o instalador.";
        Assert.Equal("### Correções\n- Corrigimos o alinhamento.\n### Novidades\n- Nova visualização compacta.", ReleaseNotesFormatter.Format(notes));
    }

    [Fact]
    public void DoesNotInventChangesWhenReleaseContainsOnlyCompareLink()
    {
        var result = ReleaseNotesFormatter.Format("## ✨ Sobre esta versão\nEsta versão do SteamNexus reúne as alterações listadas abaixo.\n**Full Changelog**: https://github.com/example/compare/v1...v2");
        Assert.Contains("não possui uma descrição detalhada", result);
    }
}
