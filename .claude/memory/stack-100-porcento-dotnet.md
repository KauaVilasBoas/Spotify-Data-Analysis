---
name: stack-100-porcento-dotnet
description: decisão explícita de resolver TUDO em .NET, inclusive o ML — propor Python, notebook ou serviço auxiliar é reabrir uma decisão já fechada
metadata:
  type: business-rule
---

Este é um **case de portfólio**, não um produto interno: o objetivo declarado é
demonstrar engenharia sênior end-to-end. Isso torna a stack parte da entrega, não
um detalhe de implementação.

Em 2026-07-28 ficou decidido: **100% .NET, com o ML feito em ML.NET — zero
Python.** O desafio original é o típico "notebook de análise"; o posicionamento
escolhido foi justamente o oposto — transformá-lo num produto de data engineering
em .NET.

**Por que isto vira memória:** a ausência de Python no repositório não se lê como
uma regra, se lê como uma lacuna. Uma sessão futura encarando treino de modelo,
EDA ou um gráfico complicado vai naturalmente sugerir scikit-learn, pandas ou um
notebook — e estaria reabrindo, sem saber, uma decisão que é o diferencial do
case. Qualquer proposta nessa direção precisa ser tratada como mudança de escopo
e levada ao usuário, não adotada por conveniência.

Vale para dependência auxiliar também: um serviço Python "só para o treino"
falha pelo mesmo motivo.
