// Carga do deploy/measure-latency.ps1. Um usuário virtual, uma passada sequencial pelas sementes,
// sempre na mesma ordem (a iteração i usa SEEDS[i]). Média e percentis saem do k6; o resumo vai ao
// stdout numa única linha "K6SUMMARY {json}" para o script PowerShell ler.
//
// Variáveis de ambiente: BASE_URL, SEEDS (ids separados por vírgula), ITERATIONS.
import http from 'k6/http';
import { Counter } from 'k6/metrics';

const seeds = __ENV.SEEDS.split(',');
const iterations = parseInt(__ENV.ITERATIONS, 10);
const non200 = new Counter('non200');

export const options = {
  scenarios: {
    pass: { executor: 'per-vu-iterations', vus: 1, iterations: iterations, maxDuration: '30m' },
  },
  summaryTrendStats: ['avg', 'med', 'p(95)', 'p(99)', 'min', 'max'],
};

export default function () {
  const url = `${__ENV.BASE_URL}/api/recommendations/track/${seeds[__ITER]}?limit=10&strategy=content`;
  const res = http.get(url, { tags: { name: 'recommendation' } });
  if (res.status !== 200) non200.add(1);
}

export function handleSummary(data) {
  const d = data.metrics.http_req_duration.values;
  const out = {
    requests: data.metrics.http_reqs.values.count,
    non200: data.metrics.non200 ? data.metrics.non200.values.count : 0,
    avg: d.avg, p50: d.med, p95: d['p(95)'], p99: d['p(99)'], min: d.min, max: d.max,
  };
  return { stdout: 'K6SUMMARY ' + JSON.stringify(out) + '\n' };
}
