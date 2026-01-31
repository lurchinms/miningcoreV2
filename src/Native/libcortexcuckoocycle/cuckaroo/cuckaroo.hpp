// Cuck(at)oo Cycle, a memory-hard proof-of-work
// Copyright (c) 2013-2019 John Tromp
#ifndef CUCKAROO_CORTEX_H
#define CUCKAROO_CORTEX_H

#include <stdint.h>				 // for types uint32_t,uint64_t
#include <string.h>				 // for functions strlen, memset
#include <stdarg.h>
#include <stdio.h>
#include <chrono>
#include <ctime>
#include "../crypto/blake2.h"
#include "../crypto/siphash.hpp"

// save some keystrokes since i'm a lazy typer
typedef uint32_t cuckaroo_cortex_u32;
typedef uint64_t cuckaroo_cortex_u64;

#ifndef CUCKAROO_CORTEX_MAX_SOLS
#define CUCKAROO_CORTEX_MAX_SOLS 4
#endif

#ifndef CUCKAROO_CORTEX_EDGE_BLOCK_BITS
#define CUCKAROO_CORTEX_EDGE_BLOCK_BITS 6
#endif
#define CUCKAROO_CORTEX_EDGE_BLOCK_SIZE (1 << CUCKAROO_CORTEX_EDGE_BLOCK_BITS)
#define CUCKAROO_CORTEX_EDGE_BLOCK_MASK (CUCKAROO_CORTEX_EDGE_BLOCK_SIZE - 1)

// proof-of-work parameters
#ifndef CUCKAROO_CORTEX_EDGEBITS
// the main parameter is the number of bits in an edge index,
// i.e. the 2-log of the number of edges
#define CUCKAROO_CORTEX_EDGEBITS 30
#endif
#ifndef CUCKAROO_CORTEX_PROOFSIZE
// the next most important parameter is the (even) length
// of the cycle to be found. a minimum of 12 is recommended
#define CUCKAROO_CORTEX_PROOFSIZE 42
#endif

#if CUCKAROO_CORTEX_EDGEBITS > 30
typedef uint64_t cuckaroo_cortex_word_t;
#elif CUCKAROO_CORTEX_EDGEBITS > 14
typedef cuckaroo_cortex_u32 cuckaroo_cortex_word_t;
#else							 // if CUCKAROO_CORTEX_EDGEBITS <= 14
typedef uint16_t cuckaroo_cortex_word_t;
#endif

// number of edges
#define CUCKAROO_CORTEX_NEDGES ((cuckaroo_cortex_word_t)1 << CUCKAROO_CORTEX_EDGEBITS)
// used to mask siphash output
#define CUCKAROO_CORTEX_EDGEMASK ((cuckaroo_cortex_word_t)CUCKAROO_CORTEX_NEDGES - 1)

enum cuckaroo_cortex_verify_code { CUCKAROO_CORTEX_POW_OK, CUCKAROO_CORTEX_POW_HEADER_LENGTH, CUCKAROO_CORTEX_POW_TOO_BIG, CUCKAROO_CORTEX_POW_TOO_SMALL, CUCKAROO_CORTEX_POW_NON_MATCHING, CUCKAROO_CORTEX_POW_BRANCH, CUCKAROO_CORTEX_POW_DEAD_END, CUCKAROO_CORTEX_POW_SHORT_CYCLE};
const char *cuckaroo_cortex_errstr[] = { "OK", "wrong header length", "edge too big", "edges not ascending", "endpoints don't match up", "branch in cycle", "cycle dead ends", "cycle too short"};

// fills buffer with CUCKAROO_CORTEX_EDGE_BLOCK_SIZE siphash outputs for block containing edge in cuckaroo graph
// return siphash output for given edge
cuckaroo_cortex_u64 cuckaroo_cortex_sipblock(siphash_keys &keys, const cuckaroo_cortex_word_t edge, cuckaroo_cortex_u64 *buf)
{
	siphash_state<> shs(keys);
	cuckaroo_cortex_word_t edge0 = edge & ~CUCKAROO_CORTEX_EDGE_BLOCK_MASK;
	for (cuckaroo_cortex_u32 i=0; i < CUCKAROO_CORTEX_EDGE_BLOCK_SIZE; i++)
	{
		shs.hash24(edge0 + i);
		buf[i] = shs.xor_lanes();
	}
	const cuckaroo_cortex_u64 last = buf[CUCKAROO_CORTEX_EDGE_BLOCK_MASK];
	for (cuckaroo_cortex_u32 i=0; i < CUCKAROO_CORTEX_EDGE_BLOCK_MASK; i++)
		buf[i] ^= last;
	return buf[edge & CUCKAROO_CORTEX_EDGE_BLOCK_MASK];
}


// verify that edges are ascending and form a cycle in header-generated graph
int cuckaroo_cortex_verify(cuckaroo_cortex_word_t edges[CUCKAROO_CORTEX_PROOFSIZE], siphash_keys &keys)
{
	cuckaroo_cortex_word_t xor0 = 0, xor1 = 0;
	cuckaroo_cortex_u64 sips[CUCKAROO_CORTEX_EDGE_BLOCK_SIZE];
	cuckaroo_cortex_word_t uvs[2*CUCKAROO_CORTEX_PROOFSIZE];

	for (cuckaroo_cortex_u32 n = 0; n < CUCKAROO_CORTEX_PROOFSIZE; n++)
	{
		if (edges[n] > CUCKAROO_CORTEX_EDGEMASK)
			return CUCKAROO_CORTEX_POW_TOO_BIG;
		if (n && edges[n] <= edges[n-1])
			return CUCKAROO_CORTEX_POW_TOO_SMALL;
		cuckaroo_cortex_u64 edge = cuckaroo_cortex_sipblock(keys, edges[n], sips);
		xor0 ^= uvs[2*n  ] = edge & CUCKAROO_CORTEX_EDGEMASK;
		xor1 ^= uvs[2*n+1] = (edge >> 32) & CUCKAROO_CORTEX_EDGEMASK;
	}
	if (xor0 | xor1)			 // optional check for obviously bad proofs
		return CUCKAROO_CORTEX_POW_NON_MATCHING;
	cuckaroo_cortex_u32 n = 0, i = 0, j;
	do							 // follow cycle
	{
		for (cuckaroo_cortex_u32 k = j = i; (k = (k+2) % (2*CUCKAROO_CORTEX_PROOFSIZE)) != i; )
		{
			if (uvs[k] == uvs[i])// find other edge endpoint identical to one at i
			{
				if (j != i)		 // already found one before
					return CUCKAROO_CORTEX_POW_BRANCH;
				j = k;
			}
		}
								 // no matching endpoint
		if (j == i) return CUCKAROO_CORTEX_POW_DEAD_END;
		i = j^1;
		n++;
	} while (i != 0);			 // must cycle back to start or we would have found branch
	return n == CUCKAROO_CORTEX_PROOFSIZE ? CUCKAROO_CORTEX_POW_OK : CUCKAROO_CORTEX_POW_SHORT_CYCLE;
}


// convenience function for extracting siphash keys from header
void cuckaroo_cortex_setheader(const char *header, const cuckaroo_cortex_u32 headerlen, siphash_keys *keys)
{
	char hdrkey[32];
	// SHA256((unsigned char *)header, headerlen, (unsigned char *)hdrkey);
	blake2b((void *)hdrkey, sizeof(hdrkey), (const void *)header, headerlen, 0, 0);
	keys->setkeys(hdrkey);
}
#endif
