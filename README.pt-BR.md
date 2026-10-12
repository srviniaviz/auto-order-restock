# Auto Order Restock

Mod local para Supermarket Simulator com BepInEx 6 IL2CPP.

Quando um produto tem uma prateleira de venda incompleta ou uma posição etiquetada e incompleta nas estantes do estoque, e o inventário não contém unidades de reserva e não há caixa com produtos na cena, o mod compra automaticamente **1 caixa** desse produto pelo carrinho normal do jogo. Ele só faz pedidos com o carrinho vazio, antes do horário limite do mercado e com saldo suficiente. Reúne no mesmo pedido 1 caixa de cada produto elegível; se atingir o limite do carrinho, inclui os demais nas próximas verificações. As caixas chegam pelo fluxo normal do jogo; os repositores continuam responsáveis por colocá-las nas prateleiras.

O mod roda apenas no modo de um jogador. Pedidos pendentes são lembrados até a caixa chegar ou por dois minutos; depois o produto pode ser comprado novamente quando acabar.

## Instalação

1. Feche o jogo e instale o Tobey's BepInEx Pack para Supermarket Simulator.
2. Extraia o ZIP na pasta do jogo. O arquivo deve ficar em `BepInEx/plugins/AutoOrderRestock/AutoOrderRestock.dll`.
3. Abra o jogo e carregue seu save.

## Controles e configuração

- **Ctrl+Alt+R:** pausa ou reativa os pedidos automáticos.
- **Ctrl+Alt+S:** pausa ou reativa pedidos acionados pelas prateleiras de venda.
- **Ctrl+Alt+E:** pausa ou reativa pedidos acionados pelas estantes do estoque.
- **Ctrl+Alt+L:** recarrega as configurações do arquivo `.cfg` sem reiniciar o jogo.

O arquivo `BepInEx/config/vinicius.autoorderrestock.cfg` é criado na primeira execução. As opções principais são `Enabled`, `SalesShelvesEnabled` e `StorageRacksEnabled` (ambos ligados por padrão), `BoxesPerProduct` (padrão 1), `CheckIntervalSeconds` (padrão 10), `DryRun` (apenas registra o que compraria) e `MaxOrdersPerSession` (0 = sem limite).

O aviso no canto superior direito mostra quando um pedido foi enviado. Para investigar falhas, consulte `BepInEx/LogOutput.log` e procure `Auto Order Restock`.

## Limites

- Somente produtos ativos, disponíveis no aplicativo do mercado e associados a uma prateleira entram na lista.
- O mod não move caixas para o estoque ou para as prateleiras.
- Se outro mod modificar o carrinho ou o inventário, a compra pode falhar. Nesse caso, pause com Ctrl+Alt+R.
