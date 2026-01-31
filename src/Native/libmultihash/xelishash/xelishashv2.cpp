#include "xelishash.hpp"
#include <stdlib.h>
#include <iostream>
#include "aes.hpp"
#include "crc32.h"

#include "../blake3/blake3.h"

#include "../chacha20/chacha20.h"

#if defined(__x86_64__)
  #include <emmintrin.h>
  #include <immintrin.h>
#elif defined(__aarch64__)
  #include <arm_neon.h>
#endif
#include <numeric>
#include <chrono>
#include <cstring>
#include <iomanip>
#include <array>
#include <cassert>
#include <chrono>

#include <sodium.h>

#ifdef _WIN32
#include <winsock2.h>
#else
#include <arpa/inet.h>
#endif

const int sign_bit_values_avx512[16][16] = {
    {0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, -1},
    {0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, -1, 0},
    {0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, -1, 0, 0},
    {0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, -1, 0, 0, 0},
    {0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, -1, 0, 0, 0, 0},
    {0, 0, 0, 0, 0, 0, 0, 0, 0, 0, -1, 0, 0, 0, 0, 0},
    {0, 0, 0, 0, 0, 0, 0, 0, 0, -1, 0, 0, 0, 0, 0, 0},
    {0, 0, 0, 0, 0, 0, 0, 0, -1, 0, 0, 0, 0, 0, 0, 0},
    {0, 0, 0, 0, 0, 0, 0, -1, 0, 0, 0, 0, 0, 0, 0, 0},
    {0, 0, 0, 0, 0, 0, -1, 0, 0, 0, 0, 0, 0, 0, 0, 0},
    {0, 0, 0, 0, 0, -1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0},
    {0, 0, 0, 0, -1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0},
    {0, 0, 0, -1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0},
    {0, 0, -1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0},
    {0, -1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0},
    {-1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0}};

alignas(32) const int sign_bit_values_avx2[8][8] = {
    {0, 0, 0, 0, 0, 0, 0, -1},
    {0, 0, 0, 0, 0, 0, -1, 0},
    {0, 0, 0, 0, 0, -1, 0, 0},
    {0, 0, 0, 0, -1, 0, 0, 0},
    {0, 0, 0, -1, 0, 0, 0, 0},
    {0, 0, -1, 0, 0, 0, 0, 0},
    {0, -1, 0, 0, 0, 0, 0, 0},
    {-1, 0, 0, 0, 0, 0, 0, 0}};

alignas(16) const int sign_bit_values_sse[4][4] = {
    {0, 0, 0, -1},
    {0, 0, -1, 0},
    {0, -1, 0, 0},
    {-1, 0, 0, 0}};

static inline uint64_t swap_bytes(uint64_t value)
{
  return ((value & 0xFF00000000000000ULL) >> 56) |
         ((value & 0x00FF000000000000ULL) >> 40) |
         ((value & 0x0000FF0000000000ULL) >> 24) |
         ((value & 0x000000FF00000000ULL) >> 8) |
         ((value & 0x00000000FF000000ULL) << 8) |
         ((value & 0x0000000000FF0000ULL) << 24) |
         ((value & 0x000000000000FF00ULL) << 40) |
         ((value & 0x00000000000000FFULL) << 56);
}

static inline void blake3(const uint8_t *input, int len, uint8_t *output) {
        blake3_hasher hasher;
	blake3_hasher_init(&hasher);
	blake3_hasher_update(&hasher, input, len);
	blake3_hasher_finalize(&hasher, output, BLAKE3_OUT_LEN);
}

static inline void aes_round(uint8_t *block, const uint8_t *key)
{
#if defined(__AES__)
  #if defined(__x86_64__)
    __m128i block_m128i = _mm_load_si128((__m128i *)block);
    __m128i key_m128i = _mm_load_si128((__m128i *)key);
    __m128i result = _mm_aesenc_si128(block_m128i, key_m128i);
    _mm_store_si128((__m128i *)block, result);
  #elif defined(__aarch64__)
    uint8x16_t blck = vld1q_u8(block);
    uint8x16_t ky = vld1q_u8(key);
    // This magic sauce is from here: https://blog.michaelbrase.com/2018/06/04/optimizing-x86-aes-intrinsics-on-armv8-a/
    uint8x16_t rslt = vaesmcq_u8(vaeseq_u8(blck, (uint8x16_t){})) ^ ky;
    vst1q_u8(block, rslt);
  #endif
#else
  aes_single_round_no_intrinsics(block, key);
#endif
}

const uint8_t chaIn[XELIS_MEMORY_SIZE_V2 * 2] = {0};

void chacha_encrypt(uint8_t *key, uint8_t *nonce, uint8_t *in, uint8_t *out, size_t bytes, uint32_t rounds)
{
	uint8_t state[48] = {0};
	ChaCha20SetKey(state, key);
	ChaCha20SetNonce(state, nonce);
	ChaCha20EncryptBytes(state, in, out, bytes, rounds);
}

void stage_1(const uint8_t *input, uint64_t *sp, size_t input_len)
{
  const size_t chunk_size = 32;
  const size_t nonce_size = 12;
  const size_t output_size = XELIS_MEMORY_SIZE_V2 * 8;
  const size_t chunks = 4;

  uint8_t *t = reinterpret_cast<uint8_t *>(sp);
  uint8_t key[chunk_size * chunks] = {0};
  uint8_t K2[32] = {0};
  uint8_t buffer[chunk_size*2] = {0};

  memcpy(key, input, input_len);
  blake3(input, input_len, buffer);

  memcpy(buffer + chunk_size, key, chunk_size);
  blake3(buffer, chunk_size*2, K2);
  chacha_encrypt(K2, buffer, NULL, t, output_size / chunks, 8);

  t += output_size / chunks;

  memcpy(buffer, K2, chunk_size);
  memcpy(buffer + chunk_size, key + chunk_size, chunk_size);
  blake3(buffer, chunk_size*2, K2);
  chacha_encrypt(K2, t - nonce_size, NULL, t, output_size / chunks, 8);

  t += output_size / chunks;

  memcpy(buffer, K2, chunk_size);
  memcpy(buffer + chunk_size, key + 2*chunk_size, chunk_size);
  blake3(buffer, chunk_size*2, K2);
  chacha_encrypt(K2, t - nonce_size, NULL, t, output_size / chunks, 8);

  t += output_size / chunks;

  memcpy(buffer, K2, chunk_size);
  memcpy(buffer + chunk_size, key + 3*chunk_size, chunk_size);
  blake3(buffer, chunk_size*2, K2);
  chacha_encrypt(K2, t - nonce_size, NULL, t, output_size / chunks, 8);

}

static inline uint64_t isqrt(uint64_t n)
{
  if (n < 2)
  {
    return n;
  }

  uint64_t x = n;
  uint64_t y = (x + 1) >> 1;

  while (y < x)
  {
    x = y;
    y = (x + n / x) >> 1;
  }

  return x;
}

#define COMBINE_UINT64(high, low) (((__uint128_t)(high) << 64) | (low))
static inline __uint128_t combine_uint64(uint64_t high, uint64_t low)
{
	return ((__uint128_t)high << 64) | low;
}

#if defined(__AVX2__)
__attribute__((target("avx2")))
void static inline uint64_to_le_bytes(uint64_t value, uint8_t *bytes) {
    // Store the result
    _mm_storel_epi64((__m128i*)bytes, _mm_shuffle_epi8(_mm_set1_epi64x(value), _mm_set_epi8(
        -1, -1, -1, -1, -1, -1, -1, -1,
        7, 6, 5, 4, 3, 2, 1, 0
    )));
}

__attribute__((target("avx2")))
uint64_t static inline le_bytes_to_uint64(const uint8_t *bytes) {
    return _mm_cvtsi128_si64(_mm_shuffle_epi8(_mm_loadu_si128((const __m128i*)bytes), _mm_set_epi8(
        15, 14, 13, 12, 11, 10, 9, 8,
        7,  6,  5,  4,  3,  2, 1, 0
    )));
}
#endif

#if defined(__x86_64__)
__attribute__((target("default")))
#endif
void static inline uint64_to_le_bytes(uint64_t value, uint8_t *bytes)
{
	for (int i = 0; i < 8; i++)
	{
		bytes[i] = value & 0xFF;
		value >>= 8;
	}
}

#if defined(__x86_64__)
__attribute__((target("default")))
#endif
uint64_t static inline le_bytes_to_uint64(const uint8_t *bytes)
{
	uint64_t value = 0;
	for (int i = 7; i >= 0; i--)
		value = (value << 8) | bytes[i];
	return value;
}

#if defined(__x86_64__)

void static inline aes_single_round(uint8_t *block, const uint8_t *key)
{
	// Perform single AES encryption round
	__m128i block_vec = _mm_aesenc_si128(_mm_loadu_si128((const __m128i *)block), _mm_loadu_si128((const __m128i *)key));
	_mm_storeu_si128((__m128i *)block, block_vec);
}

static inline uint64_t div128(__uint128_t dividend, __uint128_t divisor) {
  return dividend / divisor;
}

static inline uint64_t Divide128Div64To64(uint64_t high, uint64_t low, uint64_t divisor, uint64_t *remainder)
{
	uint64_t result;
	__asm__("divq %[v]"
			: "=a"(result), "=d"(*remainder) // Output parametrs, =a for rax, =d for rdx, [v] is an
			// alias for divisor, input paramters "a" and "d" for low and high.
			: [v] "r"(divisor), "a"(low), "d"(high));
	return result;
}

static inline uint64_t XELIS_ROTR(uint64_t x, uint32_t r)
{
	asm("rorq %%cl, %0" : "+r"(x) : "c"(r));
	return x;
}

static inline uint64_t XELIS_ROTL(uint64_t x, uint32_t r)
{
	asm("rolq %%cl, %0" : "+r"(x) : "c"(r));
	return x;
}

#else // aarch64

static inline uint64_t div128(__uint128_t dividend, __uint128_t divisor) {
    return dividend / divisor;
}

static inline uint64_t Divide128Div64To64(uint64_t high, uint64_t low, uint64_t divisor, uint64_t *remainder)
{
    // Combine high and low into a 128-bit dividend
    __uint128_t dividend = ((__uint128_t)high << 64) | low;

    // Perform division using built-in compiler functions
    *remainder = dividend % divisor;
    return dividend / divisor;
}

static inline uint64_t XELIS_ROTR(uint64_t x, uint32_t r)
{
    r %= 64;  // Ensure r is within the range [0, 63] for a 64-bit rotate
    return (x >> r) | (x << (64 - r));
}

static inline uint64_t XELIS_ROTL(uint64_t x, uint32_t r)
{
    r %= 64;  // Ensure r is within the range [0, 63] for a 64-bit rotate
    return (x << r) | (x >> (64 - r));
}

#endif

static inline uint64_t udiv(uint64_t high, uint64_t low, uint64_t divisor)
{
	uint64_t remainder;

	if (high < divisor)
	{
		return Divide128Div64To64(high, low, divisor, &remainder);
	}
	else
	{
		uint64_t qhi = Divide128Div64To64(0, high, divisor, &high);
		return Divide128Div64To64(high, low, divisor, &remainder);
	}
  return low;
}

// __attribute__((noinline))
static inline uint64_t case_0(uint64_t a, uint64_t b, uint64_t c, int r, uint64_t result, int i, int j) { 
  return XELIS_ROTL(c, i * j) ^ b; 
}
// __attribute__((noinline))
static inline uint64_t case_1(uint64_t a, uint64_t b, uint64_t c, int r, uint64_t result, int i, int j) { 
  return XELIS_ROTR(c, i * j) ^ a; 
}
// __attribute__((noinline))
static inline uint64_t case_2(uint64_t a, uint64_t b, uint64_t c, int r, uint64_t result, int i, int j) { 
  return a ^ b ^ c; 
}
// __attribute__((noinline))
static inline uint64_t case_3(uint64_t a, uint64_t b, uint64_t c, int r, uint64_t result, int i, int j) { 
  return (a + b) * c; 
}
// __attribute__((noinline))
static inline uint64_t case_4(uint64_t a, uint64_t b, uint64_t c, int r, uint64_t result, int i, int j) { 
  return (b - c) * a; 
}
// __attribute__((noinline))
static inline uint64_t case_5(uint64_t a, uint64_t b, uint64_t c, int r, uint64_t result, int i, int j) { 
  return c - a + b; 
}
// __attribute__((noinline))
static inline uint64_t case_6(uint64_t a, uint64_t b, uint64_t c, int r, uint64_t result, int i, int j) { 
  return a - b + c; 
}
// __attribute__((noinline))
static inline uint64_t case_7(uint64_t a, uint64_t b, uint64_t c, int r, uint64_t result, int i, int j) { 
  return b * c + a; 
}
// __attribute__((noinline))
static inline uint64_t case_8(uint64_t a, uint64_t b, uint64_t c, int r, uint64_t result, int i, int j) { 
  return c * a + b; 
}
// __attribute__((noinline))
static inline uint64_t case_9(uint64_t a, uint64_t b, uint64_t c, int r, uint64_t result, int i, int j) { 
  return a * b * c; 
}
// __attribute__((noinline))
static inline uint64_t case_10(uint64_t a, uint64_t b, uint64_t c, int r, uint64_t result, int i, int j) { 
  return COMBINE_UINT64(a,b) % (c | 1); 
}
// __attribute__((noinline))
static inline uint64_t case_11(uint64_t a, uint64_t b, uint64_t c, int r, uint64_t result, int i, int j) { 
  __uint128_t t2 = COMBINE_UINT64(XELIS_ROTL(result, r), a | 2);
  return (t2 > COMBINE_UINT64(b,c)) ? c : COMBINE_UINT64(b,c) % t2;
}
// __attribute__((noinline))
static inline uint64_t case_12(uint64_t a, uint64_t b, uint64_t c, int r, uint64_t result, int i, int j) { 
  return udiv(c, a, b | 4); 
}
// __attribute__((noinline))
static inline uint64_t case_13(uint64_t a, uint64_t b, uint64_t c, int r, uint64_t result, int i, int j) { 
  __uint128_t t1 = COMBINE_UINT64(XELIS_ROTL(result, r), b);
  __uint128_t t2 = COMBINE_UINT64(a, c | 8);
  return (t1 > t2) ? t1 / t2 : a ^ b;
}
// __attribute__((noinline))
static inline uint64_t case_14(uint64_t a, uint64_t b, uint64_t c, int r, uint64_t result, int i, int j) { 
  return (COMBINE_UINT64(b,a) * c) >> 64; 
}
// __attribute__((noinline))
static inline uint64_t case_15(uint64_t a, uint64_t b, uint64_t c, int r, uint64_t result, int i, int j) { 
  return (COMBINE_UINT64(a,c) * COMBINE_UINT64(XELIS_ROTR(result, r), b)) >> 64; 
}

typedef uint64_t (*operation_func)(uint64_t, uint64_t, uint64_t, int, uint64_t, int, int);

operation_func operations[] = {
    case_0, case_1, case_2, case_3, case_4, case_5, case_6, case_7,
    case_8, case_9, case_10, case_11, case_12, case_13, case_14, case_15,
    // Add other functions for cases 10-15
};

void stage_3(uint64_t *scratch_pad)
{
    const uint8_t key[17] = "xelishash-pow-v2";
    uint8_t block[16] = {0};

    uint64_t *mem_buffer_a = scratch_pad;
    uint64_t *mem_buffer_b = scratch_pad + XELIS_BUFFER_SIZE_V2;

    uint64_t addr_a = mem_buffer_b[XELIS_BUFFER_SIZE_V2 - 1];
    uint64_t addr_b = mem_buffer_a[XELIS_BUFFER_SIZE_V2 - 1] >> 32;
    size_t r = 0;


    #pragma GCC unroll 3
    for (size_t i = 0; i < XELIS_SCRATCHPAD_ITERS_V2; ++i) {
        uint64_t mem_a = mem_buffer_a[addr_a % XELIS_BUFFER_SIZE_V2];
        uint64_t mem_b = mem_buffer_b[addr_b % XELIS_BUFFER_SIZE_V2];

        // std::copy(&mem_b, &mem_b + 8, block);
        // std::copy(&mem_a, &mem_a + 8, block + 8);

        uint64_to_le_bytes(mem_b, block);
		    uint64_to_le_bytes(mem_a, block + 8);

        aes_round(block, key);

        uint64_t hash1 = 0, hash2 = 0;
        // hash1 = ((uint64_t*)block)[0]; // simple assignment, slower than SIMD on my CPU
        hash1 = le_bytes_to_uint64(block);
        // std::copy(block, block + 8, &hash1);
        // hash1 = _byteswap_uint64(hash1);
        hash2 = mem_a ^ mem_b;

        addr_a = ~(hash1 ^ hash2);

        // printf("pre result: %llu\n", result);

        for (size_t j = 0; j < XELIS_BUFFER_SIZE_V2; ++j) {
            uint64_t a = mem_buffer_a[(addr_a % XELIS_BUFFER_SIZE_V2)];
            uint64_t b = mem_buffer_b[~XELIS_ROTR(addr_a, r) % XELIS_BUFFER_SIZE_V2];
            uint64_t c = (r < XELIS_BUFFER_SIZE_V2) ? mem_buffer_a[r] : mem_buffer_b[r - XELIS_BUFFER_SIZE_V2];
            r = (r+1) % XELIS_MEMORY_SIZE_V2;

            // printf("a %llu, b %llu, c %llu, ", a, b, c);
            uint64_t v;
            uint32_t idx = XELIS_ROTL(addr_a, (uint32_t)c) & 0xF;
            v = operations[idx](a,b,c,r,addr_a,i,j);

            addr_a = XELIS_ROTL(addr_a ^ v, 1);

            uint64_t t = mem_buffer_a[XELIS_BUFFER_SIZE_V2 - j - 1] ^ addr_a;
            mem_buffer_a[XELIS_BUFFER_SIZE_V2 - j - 1] = t;
            mem_buffer_b[j] ^= XELIS_ROTR(t, (uint32_t)addr_a);
        }
        // printf("post result: %llu\n", result);
        addr_b = isqrt(addr_a);
    }

}

void xelis_hash_v2(const unsigned char *input, uint32_t inputLen, unsigned char *hashResult)
{
  uint64_t scratchPad[XELIS_MEMORY_SIZE_V2] = {0};

  stage_1(input, scratchPad, inputLen);
  stage_3(scratchPad);
  blake3((uint8_t*)scratchPad, XELIS_MEMORY_SIZE_V2 * 8, hashResult);
}
