// Teste de carga dos SLOs 2, 3 e 4: 50 req/s no GET /api/consolidado.
//
//   docker compose up -d
//   docker compose --profile load run --rm k6
//
// Os thresholds são os SLOs: se o k6 sair com código != 0, o SLO foi violado.

import http from 'k6/http';
import { check, fail, sleep } from 'k6';

const LANCAMENTOS = __ENV.LANCAMENTOS_URL || 'http://localhost:5001';
const CONSOLIDADO = __ENV.CONSOLIDADO_URL || 'http://localhost:5002';
const COMERCIANTE = __ENV.COMERCIANTE_ID || '11111111-1111-1111-1111-111111111111';
const DIAS = 7;

export const options = {
  scenarios: {
    consulta: {
      executor: 'constant-arrival-rate',
      rate: 50,
      timeUnit: '1s',
      duration: __ENV.DURACAO || '60s',
      preAllocatedVUs: 10,
      maxVUs: 50,
    },
  },
  // A tag isola as consultas: as requisições do setup (token e seed) entram
  // nas métricas globais do k6, mas não podem contar contra o SLO medido.
  thresholds: {
    'http_req_failed{alvo:consolidado}': ['rate<0.05'],
    'http_req_duration{alvo:consolidado}': ['p(95)<100', 'p(99)<300'],
  },
};

const cabecalhoJson = { 'Content-Type': 'application/json' };

// O dia contábil é o de America/Sao_Paulo (UTC-3). Usar a data UTC faria o
// lançamento ser rejeitado como futuro nas primeiras horas do dia.
function dataDoDia(diasAtras) {
  const instante = Date.now() - 3 * 3600 * 1000 - diasAtras * 86400 * 1000;
  return new Date(instante).toISOString().slice(0, 10);
}

export function setup() {
  const autenticacao = http.post(
    `${LANCAMENTOS}/api/token`,
    JSON.stringify({ comercianteId: COMERCIANTE }),
    { headers: cabecalhoJson });

  if (autenticacao.status !== 200) {
    fail(`falha ao obter token: ${autenticacao.status} ${autenticacao.body}`);
  }

  const token = autenticacao.json('token');
  const datas = [];

  // Chave de idempotência determinística: reexecutar o teste não duplica o seed.
  for (let dia = 0; dia < DIAS; dia++) {
    const data = dataDoDia(dia);
    datas.push(data);

    const criacao = http.post(
      `${LANCAMENTOS}/api/lancamentos`,
      JSON.stringify({
        comercianteId: COMERCIANTE,
        tipo: 'CREDITO',
        valor: 100.00,
        moeda: 'BRL',
        dataCompetencia: data,
        descricao: 'seed do teste de carga',
      }),
      { headers: { ...cabecalhoJson, Authorization: `Bearer ${token}`, 'Idempotency-Key': `carga-${data}` } });

    if (criacao.status !== 201 && criacao.status !== 200) {
      fail(`falha ao semear ${data}: ${criacao.status} ${criacao.body}`);
    }
  }

  sleep(5); // consistência eventual: o read model precisa refletir o seed
  return { token, datas };
}

export default function (dados) {
  // Sorteia entre os dias semeados: consultar sempre a mesma chave mediria um
  // único cache hit repetido, não o comportamento do serviço.
  const data = dados.datas[Math.floor(Math.random() * dados.datas.length)];

  const resposta = http.get(
    `${CONSOLIDADO}/api/consolidado/${COMERCIANTE}/${data}?moeda=BRL`,
    {
      headers: { Authorization: `Bearer ${dados.token}` },
      tags: { alvo: 'consolidado' },
    });

  check(resposta, { 'consulta respondeu 200': (r) => r.status === 200 });
}
