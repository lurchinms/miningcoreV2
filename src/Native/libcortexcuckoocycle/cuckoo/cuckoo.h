// Cuckoo Cycle, a memory-hard proof-of-work
// Copyright (c) 2013-2017 John Tromp
#ifndef CUCKOO_CORTEX_H
#define CUCKOO_CORTEX_H

#include <stdint.h> // for types uint32_t,uint64_t
#include <string.h> // for functions strlen, memset
#include "../crypto/blake2.h"
#include "../crypto/siphash.hpp"

#ifdef SIPHASH_COMPAT
#include <stdio.h>
#endif

// save some keystrokes since i'm a lazy typer
typedef uint32_t cuckoo_cortex_u32;
typedef uint64_t cuckoo_cortex_u64;

// proof-of-work parameters
#ifndef CUCKOO_CORTEX_EDGEBITS
// the main parameter is the 2-log of the graph size,
// which is the size in bits of the node identifiers
#define CUCKOO_CORTEX_EDGEBITS 30
#endif
#ifndef CUCKOO_CORTEX_PROOFSIZE
// the next most important parameter is the (even) length
// of the cycle to be found. a minimum of 12 is recommended
#define CUCKOO_CORTEX_PROOFSIZE 42
#endif

#if CUCKOO_CORTEX_EDGEBITS > 32
typedef cuckoo_cortex_u64 cuckoo_cortex_edge_t;
#else
typedef cuckoo_cortex_u32 cuckoo_cortex_edge_t;
#endif
#if CUCKOO_CORTEX_EDGEBITS > 31
typedef cuckoo_cortex_u64 cuckoo_cortex_node_t;
#else
typedef cuckoo_cortex_u32 cuckoo_cortex_node_t;
#endif

// number of edges
#define CUCKOO_CORTEX_NEDGES ((cuckoo_cortex_node_t)1 << CUCKOO_CORTEX_EDGEBITS)
// used to mask siphash output
#define CUCKOO_CORTEX_EDGEMASK ((cuckoo_cortex_edge_t)CUCKOO_CORTEX_NEDGES - 1)

// generate edge endpoint in cuckoo graph without partition bit
cuckoo_cortex_node_t cuckoo_cortex_sipnode(siphash_keys *keys, cuckoo_cortex_edge_t edge, cuckoo_cortex_u32 uorv) {
  return keys->siphash24(2*edge + uorv) & CUCKOO_CORTEX_EDGEMASK;
}

enum cuckoo_cortex_verify_code { CUCKOO_CORTEX_POW_OK, CUCKOO_CORTEX_POW_HEADER_LENGTH, CUCKOO_CORTEX_POW_TOO_BIG, CUCKOO_CORTEX_POW_TOO_SMALL, CUCKOO_CORTEX_POW_NON_MATCHING, CUCKOO_CORTEX_POW_BRANCH, CUCKOO_CORTEX_POW_DEAD_END, CUCKOO_CORTEX_POW_SHORT_CYCLE};
const char *cuckoo_cortex_errstr[] = { "OK", "wrong header length", "edge too big", "edges not ascending", "endpoints don't match up", "branch in cycle", "cycle dead ends", "cycle too short"};

// verify that edges are ascending and form a cycle in header-generated graph
int cuckoo_cortex_verify(cuckoo_cortex_edge_t edges[CUCKOO_CORTEX_PROOFSIZE], siphash_keys *keys) {
  cuckoo_cortex_node_t uvs[2*CUCKOO_CORTEX_PROOFSIZE];
  cuckoo_cortex_node_t xor0 = 0, xor1  =0;
  for (cuckoo_cortex_u32 n = 0; n < CUCKOO_CORTEX_PROOFSIZE; n++) {
    if (edges[n] > CUCKOO_CORTEX_EDGEMASK)
      return CUCKOO_CORTEX_POW_TOO_BIG;
    if (n && edges[n] <= edges[n-1])
      return CUCKOO_CORTEX_POW_TOO_SMALL;
    xor0 ^= uvs[2*n  ] = cuckoo_cortex_sipnode(keys, edges[n], 0);
    xor1 ^= uvs[2*n+1] = cuckoo_cortex_sipnode(keys, edges[n], 1);
  }
  if (xor0|xor1)              // optional check for obviously bad proofs
    return CUCKOO_CORTEX_POW_NON_MATCHING;
  cuckoo_cortex_u32 n = 0, i = 0, j;
  do {                        // follow cycle
    for (cuckoo_cortex_u32 k = j = i; (k = (k+2) % (2*CUCKOO_CORTEX_PROOFSIZE)) != i; ) {
      if (uvs[k] == uvs[i]) { // find other edge endpoint identical to one at i
        if (j != i)           // already found one before
          return CUCKOO_CORTEX_POW_BRANCH;
        j = k;
      }
    }
    if (j == i) return CUCKOO_CORTEX_POW_DEAD_END;  // no matching endpoint
    i = j^1;
    n++;
  } while (i != 0);           // must cycle back to start or we would have found branch
  return n == CUCKOO_CORTEX_PROOFSIZE ? CUCKOO_CORTEX_POW_OK : CUCKOO_CORTEX_POW_SHORT_CYCLE;
}

// convenience function for extracting siphash keys from header
void cuckoo_cortex_setheader(const char *header, const cuckoo_cortex_u32 headerlen, siphash_keys *keys) {
  char hdrkey[32];
  // SHA256((unsigned char *)header, headerlen, (unsigned char *)hdrkey);
  blake2b((void *)hdrkey, sizeof(hdrkey), (const void *)header, headerlen, 0, 0);
#ifdef SIPHASH_COMPAT
  cuckoo_cortex_u64 *k = (cuckoo_cortex_u64 *)hdrkey;
  cuckoo_cortex_u64 k0 = k[0];
  cuckoo_cortex_u64 k1 = k[1];
  //printf("k0 k1 %lx %lx\n", k0, k1);
  k[0] = k0 ^ 0x736f6d6570736575ULL;
  k[1] = k1 ^ 0x646f72616e646f6dULL;
  k[2] = k0 ^ 0x6c7967656e657261ULL;
  k[3] = k1 ^ 0x7465646279746573ULL;
#endif
  keys->setkeys(hdrkey);
}

// edge endpoint in cuckoo graph with partition bit
cuckoo_cortex_edge_t cuckoo_cortex_sipnode_(siphash_keys *keys, cuckoo_cortex_edge_t edge, cuckoo_cortex_u32 uorv) {
  return cuckoo_cortex_sipnode(keys, edge, uorv) << 1 | uorv;
}
#endif
