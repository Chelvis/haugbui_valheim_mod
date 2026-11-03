# Haugbui

Um servo morto-vivo que cuida do serviço chato da base. Construa um túmulo perto da sua cama e ele organiza baús,
busca o que você pedir, colhe a plantação e alimenta as forjas enquanto você sai pelo mundo.

A versão marcada no git ainda é a **0.0.10**. O texto em inglês, com a instalação e o desenvolvimento, está no
[README.md](README.md).

## O que ele faz

| Tarefa | Como funciona |
|---|---|
| Organizar o estoque | Coloca o item no baú que **já tem** aquele item. Baú vazio não recebe nada, salvo se você gravar um filtro. |
| Baú de coleta | Você joga qualquer coisa nele; ele guarda. |
| Baú de entrega | Você pede "40 madeira, 20 resina"; ele junta nos seus baús e traz. |
| Chão e colheita | Recolhe o que está no chão, colhe plantação madura, colmeias e ninhos. Uma pilha pesada demais é levada em partes. |
| Produção | Alimenta fundição, fornalha, carvão, moinho, roda de fiar, refinaria de eitr, fermentador, fornos e estações de cozinha, e o combustível da banheira e do gerador de escudo. |
| Defesa | Só com a espada do rei. Ele defende o raio do túmulo. Sem combate, foge para o túmulo. |
| Sono | Trabalha a noite que você pula dormindo. Longe da base, ele ainda não trabalha. |

Cada turno é pago com uma oferenda no túmulo, e só quando há trabalho. Ele não cria nem apaga itens, não mexe em
equipamento, respeita proteções e baús privados, e só tira o item da origem depois de colocá-lo no destino.

O túmulo sobe de nível com cinco melhorias, em qualquer ordem. A produção abre no nível 2. A espada liga o combate.
O altar faz ele trabalhar de dia. O altar não dá resistência.

## Instalar

1. Pelo r2modman, instale o BepInExPack Valheim 5.4.2351 e o Jötunn 2.30.2.
2. Instale o Haugbui do mesmo jeito, ou copie a pasta `Haugbui` do build Release para `BepInEx\plugins`.
3. Abra o jogo. O martelo ganha a aba **Haugbui**.

O jogo normal não precisa de configuração. O editor dentro do jogo é o Configuration Manager (F1) e não vem neste
pacote.

## O que o mundo guarda se o mod sumir

As construções são peças normais do jogo. Sem a DLL, a peça, o baú e os itens continuam. O servo, a aparência
própria e a automação somem até a DLL voltar. As chaves do Haugbui ficam na peça. Se você desmontar a peça sem o
mod, esse estado se perde e os itens caem como em qualquer baú.

Ainda **não** foi feito o teste de fechar o jogo, tirar a DLL, carregar, salvar e colocar a DLL de volta. Até isso,
a cópia da pasta do mundo é o caminho de volta. Essa cópia é opcional e começa desligada (`BackupWorldOnLoad`).
Ao carregar, um aviso aparece se faltam túmulos que o mundo tinha registrado.

## Até onde ele alcança

Ele só mexe no que está carregado perto de você. Na distância de simulação padrão isso é cerca de 128 m. No ajuste
mais baixo, cerca de 64 m. Um raio maior não alcança baú que não está carregado; o painel do túmulo avisa quando o
raio está só em parte carregado.

## Outros mods de logística

Não deixe outro mod mover os mesmos itens. AzuAutoStore, MultiUserChest e Automatics podem pegar a pilha no mesmo
instante e duplicar ou perder o item. Use baús diferentes, ou deixe esses mods desligados.
