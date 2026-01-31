#pragma once

#include <inttypes.h>
#include <algorithm>
#include <numeric>

#define XELIS_USE_AVX512 4
#define XELIS_USE_AVX2 3
#define XELIS_USE_SSE2 2
#define XELIS_USE_SCALAR 1

#define XELIS_BATCHSIZE_V2 1

const uint16_t XELIS_MEMORY_SIZE = 32768;
const size_t XELIS_MEMORY_SIZE_V2 = 429*128;

const uint16_t XELIS_SCRATCHPAD_ITERS = 5000;
const uint16_t XELIS_SCRATCHPAD_ITERS_V2 = 3;

const unsigned char XELIS_ITERS = 1;
const uint16_t XELIS_BUFFER_SIZE = 42;
const uint16_t XELIS_BUFFER_SIZE_V2 = XELIS_MEMORY_SIZE_V2 / 2;

const uint16_t XELIS_SLOT_LENGTH = 256;
const int XELIS_TEMPLATE_SIZE = 112;

const unsigned char XELIS_KECCAK_WORDS = 25;
const unsigned char XELIS_BYTES_ARRAY_INPUT = XELIS_KECCAK_WORDS * 8;
const unsigned char XELIS_HASH_SIZE = 32;
const uint16_t XELIS_STAGE_1_MAX = XELIS_MEMORY_SIZE / XELIS_KECCAK_WORDS;

typedef struct xelis_BlockMiner {
    uint8_t header_work_hash[32];
    uint64_t timestamp;
    uint64_t nonce;
    uint8_t miner[32];
    uint8_t extra_nonce[32];
    // Other fields and methods...
} xelis_BlockMiner;

void xelis_hash(const unsigned char *input, uint32_t inputLen, unsigned char *hashResult);
void xelis_hash_v2(const unsigned char *input, uint32_t inputLen, unsigned char *hashResult);
