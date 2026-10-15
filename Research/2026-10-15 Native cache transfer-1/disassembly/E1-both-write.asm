?write@@YAXAEAV?$ValueAccessorImpl@V?$Tree@V?$RootNode@V?$InternalNode@V?$InternalNode@V?$LeafNode@G$02@tree@v13_0@openvdb@@$03@tree@v13_0@openvdb@@$04@tree@v13_0@openvdb@@@tree@v13_0@openvdb@@@tree@v13_0@openvdb@@$00XU?$index_sequence@$0A@$00$01@34@@tree@v13_0@openvdb@@AEBV?$vector@VCoord@math@v13_0@openvdb@@V?$allocator@VCoord@math@v13_0@openvdb@@@std@@@std@@HH_N@Z PROC ; write, COMDAT

; 74   : {

$LN363:
	push	rbx
	push	rbp
	push	rsi
	push	rdi
	push	r12
	push	r14
	push	r15
	sub	rsp, 48					; 00000030H
	mov	rbx, rcx
	mov	r14, rdx
; File C:\Program Files\Microsoft Visual Studio\18\Insiders\VC\Tools\MSVC\14.51.36231\include\vector

; 1922 :         return static_cast<size_type>(_My_data._Mylast - _My_data._Myfirst);

	mov	rcx, QWORD PTR [rdx+8]
	mov	rax, 3074457345618258603		; 2aaaaaaaaaaaaaabH
	sub	rcx, QWORD PTR [rdx]
; File D:\SourceCode\Tedd.OpenVDB\benchmarks\NativeCacheTransfer\main.cpp

; 76   :     if (partial) {

	xor	edi, edi
; File C:\Program Files\Microsoft Visual Studio\18\Insiders\VC\Tools\MSVC\14.51.36231\include\vector

; 1922 :         return static_cast<size_type>(_My_data._Mylast - _My_data._Myfirst);

	imul	rcx
; File D:\SourceCode\Tedd.OpenVDB\benchmarks\NativeCacheTransfer\main.cpp

; 74   : {

	mov	r15d, r9d
	mov	ebp, r8d
; File C:\Program Files\Microsoft Visual Studio\18\Insiders\VC\Tools\MSVC\14.51.36231\include\vector

; 1922 :         return static_cast<size_type>(_My_data._Mylast - _My_data._Myfirst);

	sar	rdx, 1
	mov	r12, rdx
	shr	r12, 63					; 0000003fH
; File D:\SourceCode\Tedd.OpenVDB\benchmarks\NativeCacheTransfer\main.cpp

; 75   :     const size_t mask = coords.size() - 1;

	dec	r12
	add	r12, rdx

; 76   :     if (partial) {

	cmp	BYTE PTR partial$[rsp], dil
	je	$LN8@write

; 77   :         for (int i = 0; i < count; ++i) {

	test	r8d, r8d
	jle	$LN6@write
	npad	8
$LL4@write:

; 78   :             const size_t index = (size_t(i) << 1) & mask;

	mov	ecx, edi
; File D:\source\openvdb\openvdb\openvdb\TypeList.h

; 531  :         if (pred(Idx)) op(std::get<Idx>(tup));

	xor	edx, edx
; File D:\SourceCode\Tedd.OpenVDB\benchmarks\NativeCacheTransfer\main.cpp

; 78   :             const size_t index = (size_t(i) << 1) & mask;

	add	rcx, rcx
; File D:\source\openvdb\openvdb\openvdb\TypeList.h

; 909  :         typelist_internal::TSEvalFirstPredImpl

	mov	QWORD PTR $T11[rsp], rbx
; File D:\SourceCode\Tedd.OpenVDB\benchmarks\NativeCacheTransfer\main.cpp

; 78   :             const size_t index = (size_t(i) << 1) & mask;

	and	rcx, r12

; 79   :             a.setValueOn(coords[index], uint16_t(1 + ((index + phase) & 7)));

	lea	eax, DWORD PTR [r15+rcx]
	and	ax, 7
; File C:\Program Files\Microsoft Visual Studio\18\Insiders\VC\Tools\MSVC\14.51.36231\include\vector

; 1952 :         return _My_data._Myfirst[_Pos];

	lea	rcx, QWORD PTR [rcx+rcx*2]
; File D:\SourceCode\Tedd.OpenVDB\benchmarks\NativeCacheTransfer\main.cpp

; 79   :             a.setValueOn(coords[index], uint16_t(1 + ((index + phase) & 7)));

	inc	ax
	mov	WORD PTR $T13[rsp], ax
; File C:\Program Files\Microsoft Visual Studio\18\Insiders\VC\Tools\MSVC\14.51.36231\include\vector

; 1952 :         return _My_data._Myfirst[_Pos];

	mov	rax, QWORD PTR [r14]
	lea	rsi, QWORD PTR [rax+rcx*4]
; File D:\source\openvdb\openvdb\openvdb\TypeList.h

; 531  :         if (pred(Idx)) op(std::get<Idx>(tup));

	lea	rcx, QWORD PTR $T11[rsp]

; 909  :         typelist_internal::TSEvalFirstPredImpl

	mov	QWORD PTR $T11[rsp+8], rsi

; 531  :         if (pred(Idx)) op(std::get<Idx>(tup));

	call	??$?RU?$integral_constant@_K$0A@@std@@@<lambda_ca3903ac405762ed18e3c24717acdf32>@@QEBA_NU?$integral_constant@_K$0A@@std@@@Z ; <lambda_ca3903ac405762ed18e3c24717acdf32>::operator()<std::integral_constant<unsigned __int64,0> >
	test	al, al
	je	SHORT $LN32@write
; File D:\Workspaces\AI\openvdb-native-cache-transfer-20261015\overlay\openvdb\tree\LeafNode.h

; 1072 :     return ((xyz[0] & (DIM-1u)) << 2*Log2Dim)

	mov	ecx, DWORD PTR [rsi]
	mov	eax, DWORD PTR [rsi+4]
	and	ecx, 7
; File D:\source\openvdb\openvdb\openvdb\TypeList.h

; 531  :         if (pred(Idx)) op(std::get<Idx>(tup));

	mov	r8, QWORD PTR [rbx+600]
; File D:\Workspaces\AI\openvdb-native-cache-transfer-20261015\overlay\openvdb\tree\LeafNode.h

; 1072 :     return ((xyz[0] & (DIM-1u)) << 2*Log2Dim)

	and	eax, 7
	lea	ecx, DWORD PTR [rax+rcx*8]
	mov	eax, DWORD PTR [rsi+8]
	and	eax, 7
	lea	edx, DWORD PTR [rax+rcx*8]
; File D:\Workspaces\AI\openvdb-native-cache-transfer-20261015\overlay\openvdb\tree\ValueAccessor.h

; 560  :                     const_cast<ValueType&>(LeafCacheT::buffer()[offset]) = value;

	mov	rcx, QWORD PTR [rbx+16]
	movzx	eax, WORD PTR $T13[rsp]
	mov	WORD PTR [rcx+rdx*2], ax

; 561  : #ifdef EXPERIMENT_DENSE_MASK
; 562  :                     // This experiment targets ordinary leaves with at least 64 voxels.
; 563  :                     // Smaller leaves retain their original mask API and behavior.
; 564  :                     if constexpr (NodeType::LOG2DIM >= 2) {
; 565  :                         if (node->getValueMask().template getWord<Index64>(offset >> 6) != ~Index64(0))

	mov	ecx, edx
	shr	rcx, 6
	mov	rax, QWORD PTR [r8+rcx*8+16]
	cmp	rax, -1
	je	$LN2@write
; File D:\source\openvdb\openvdb\openvdb\util\NodeMasks.h

; 455  :         mWords[n >> 6] |=  Word(1) << (n & 63);

	and	edx, 63					; 0000003fH
	bts	rax, rdx
	mov	QWORD PTR [r8+rcx*8+16], rax
; File D:\source\openvdb\openvdb\openvdb\TypeList.h

; 531  :         if (pred(Idx)) op(std::get<Idx>(tup));

	jmp	$LN2@write
$LN32@write:

; 532  :         else TSEvalFirstPredImpl<Pred, OpT, TupleT, Iter+1, End>(pred, op, tup);

	mov	r8, QWORD PTR $T11[rsp+8]
	mov	rcx, QWORD PTR $T11[rsp]
; File D:\source\openvdb\openvdb\openvdb\math\Coord.h

; 134  :     Int32 operator[](size_t i) const { OPENVDB_ASSERT(i < 3); return mVec[i]; }

	mov	r9d, DWORD PTR [r8]
; File D:\Workspaces\AI\openvdb-native-cache-transfer-20261015\overlay\openvdb\tree\ValueAccessor.h

; 968  :             const bool matches = (xyz[0] & ~Coord::ValueType(NodeT::DIM-1)) == mKeys[Idx][0]

	mov	eax, r9d
	and	eax, -128				; ffffffffffffff80H
	cmp	eax, DWORD PTR [rcx+548]
	jne	SHORT $LN62@write
	mov	eax, DWORD PTR [r8+4]
	and	eax, -128				; ffffffffffffff80H
	cmp	eax, DWORD PTR [rcx+552]
	jne	SHORT $LN62@write
	mov	eax, DWORD PTR [r8+8]
	and	eax, -128				; ffffffffffffff80H
	cmp	eax, DWORD PTR [rcx+556]
	jne	SHORT $LN62@write

; 575  :                     const_cast<NodeType*>(node)->setValueAndCache(xyz, value, *this);

	mov	rcx, QWORD PTR [rbx+592]
	lea	r8, QWORD PTR $T13[rsp]
	mov	r9, rbx
	mov	rdx, rsi
	call	??$setValueAndCache@V?$ValueAccessorImpl@V?$Tree@V?$RootNode@V?$InternalNode@V?$InternalNode@V?$LeafNode@G$02@tree@v13_0@openvdb@@$03@tree@v13_0@openvdb@@$04@tree@v13_0@openvdb@@@tree@v13_0@openvdb@@@tree@v13_0@openvdb@@$00XU?$index_sequence@$0A@$00$01@34@@tree@v13_0@openvdb@@@?$InternalNode@V?$LeafNode@G$02@tree@v13_0@openvdb@@$03@tree@v13_0@openvdb@@QEAAXAEBVCoord@math@23@AEBGAEAV?$ValueAccessorImpl@V?$Tree@V?$RootNode@V?$InternalNode@V?$InternalNode@V?$LeafNode@G$02@tree@v13_0@openvdb@@$03@tree@v13_0@openvdb@@$04@tree@v13_0@openvdb@@@tree@v13_0@openvdb@@@tree@v13_0@openvdb@@$00XU?$index_sequence@$0A@$00$01@34@@123@@Z ; openvdb::v13_0::tree::InternalNode<openvdb::v13_0::tree::LeafNode<unsigned short,3>,4>::setValueAndCache<openvdb::v13_0::tree::ValueAccessorImpl<openvdb::v13_0::tree::Tree<openvdb::v13_0::tree::RootNode<openvdb::v13_0::tree::InternalNode<openvdb::v13_0::tree::InternalNode<openvdb::v13_0::tree::LeafNode<unsigned short,3>,4>,5> > >,1,void,openvdb::v13_0::index_sequence<0,1,2> > >
; File D:\source\openvdb\openvdb\openvdb\TypeList.h

; 531  :         if (pred(Idx)) op(std::get<Idx>(tup));

	jmp	SHORT $LN2@write
$LN62@write:
; File D:\Workspaces\AI\openvdb-native-cache-transfer-20261015\overlay\openvdb\tree\ValueAccessor.h

; 968  :             const bool matches = (xyz[0] & ~Coord::ValueType(NodeT::DIM-1)) == mKeys[Idx][0]

	and	r9d, -4096				; fffffffffffff000H
	cmp	r9d, DWORD PTR [rcx+560]
	jne	SHORT $LN114@write
	mov	eax, DWORD PTR [r8+4]
	and	eax, -4096				; fffffffffffff000H
	cmp	eax, DWORD PTR [rcx+564]
	jne	SHORT $LN114@write
	mov	eax, DWORD PTR [r8+8]
	and	eax, -4096				; fffffffffffff000H
	cmp	eax, DWORD PTR [rcx+568]
	jne	SHORT $LN114@write

; 575  :                     const_cast<NodeType*>(node)->setValueAndCache(xyz, value, *this);

	mov	rcx, QWORD PTR [rbx+584]
	lea	r8, QWORD PTR $T13[rsp]
	mov	r9, rbx
	mov	rdx, rsi
	call	??$setValueAndCache@V?$ValueAccessorImpl@V?$Tree@V?$RootNode@V?$InternalNode@V?$InternalNode@V?$LeafNode@G$02@tree@v13_0@openvdb@@$03@tree@v13_0@openvdb@@$04@tree@v13_0@openvdb@@@tree@v13_0@openvdb@@@tree@v13_0@openvdb@@$00XU?$index_sequence@$0A@$00$01@34@@tree@v13_0@openvdb@@@?$InternalNode@V?$InternalNode@V?$LeafNode@G$02@tree@v13_0@openvdb@@$03@tree@v13_0@openvdb@@$04@tree@v13_0@openvdb@@QEAAXAEBVCoord@math@23@AEBGAEAV?$ValueAccessorImpl@V?$Tree@V?$RootNode@V?$InternalNode@V?$InternalNode@V?$LeafNode@G$02@tree@v13_0@openvdb@@$03@tree@v13_0@openvdb@@$04@tree@v13_0@openvdb@@@tree@v13_0@openvdb@@@tree@v13_0@openvdb@@$00XU?$index_sequence@$0A@$00$01@34@@123@@Z ; openvdb::v13_0::tree::InternalNode<openvdb::v13_0::tree::InternalNode<openvdb::v13_0::tree::LeafNode<unsigned short,3>,4>,5>::setValueAndCache<openvdb::v13_0::tree::ValueAccessorImpl<openvdb::v13_0::tree::Tree<openvdb::v13_0::tree::RootNode<openvdb::v13_0::tree::InternalNode<openvdb::v13_0::tree::InternalNode<openvdb::v13_0::tree::LeafNode<unsigned short,3>,4>,5> > >,1,void,openvdb::v13_0::index_sequence<0,1,2> > >
; File D:\source\openvdb\openvdb\openvdb\TypeList.h

; 531  :         if (pred(Idx)) op(std::get<Idx>(tup));

	jmp	SHORT $LN2@write
$LN114@write:
; File D:\Workspaces\AI\openvdb-native-cache-transfer-20261015\overlay\openvdb\tree\ValueAccessor.h

; 575  :                     const_cast<NodeType*>(node)->setValueAndCache(xyz, value, *this);

	mov	rcx, QWORD PTR [rbx+576]
	lea	r8, QWORD PTR $T13[rsp]
	mov	r9, rbx
	mov	rdx, rsi
	call	??$setValueAndCache@V?$ValueAccessorImpl@V?$Tree@V?$RootNode@V?$InternalNode@V?$InternalNode@V?$LeafNode@G$02@tree@v13_0@openvdb@@$03@tree@v13_0@openvdb@@$04@tree@v13_0@openvdb@@@tree@v13_0@openvdb@@@tree@v13_0@openvdb@@$00XU?$index_sequence@$0A@$00$01@34@@tree@v13_0@openvdb@@@?$RootNode@V?$InternalNode@V?$InternalNode@V?$LeafNode@G$02@tree@v13_0@openvdb@@$03@tree@v13_0@openvdb@@$04@tree@v13_0@openvdb@@@tree@v13_0@openvdb@@QEAAXAEBVCoord@math@23@AEBGAEAV?$ValueAccessorImpl@V?$Tree@V?$RootNode@V?$InternalNode@V?$InternalNode@V?$LeafNode@G$02@tree@v13_0@openvdb@@$03@tree@v13_0@openvdb@@$04@tree@v13_0@openvdb@@@tree@v13_0@openvdb@@@tree@v13_0@openvdb@@$00XU?$index_sequence@$0A@$00$01@34@@123@@Z ; openvdb::v13_0::tree::RootNode<openvdb::v13_0::tree::InternalNode<openvdb::v13_0::tree::InternalNode<openvdb::v13_0::tree::LeafNode<unsigned short,3>,4>,5> >::setValueAndCache<openvdb::v13_0::tree::ValueAccessorImpl<openvdb::v13_0::tree::Tree<openvdb::v13_0::tree::RootNode<openvdb::v13_0::tree::InternalNode<openvdb::v13_0::tree::InternalNode<openvdb::v13_0::tree::LeafNode<unsigned short,3>,4>,5> > >,1,void,openvdb::v13_0::index_sequence<0,1,2> > >
$LN2@write:
; File D:\SourceCode\Tedd.OpenVDB\benchmarks\NativeCacheTransfer\main.cpp

; 77   :         for (int i = 0; i < count; ++i) {

	inc	edi
	cmp	edi, ebp
	jl	$LL4@write

; 84   :     }
; 85   : }

	add	rsp, 48					; 00000030H
	pop	r15
	pop	r14
	pop	r12
	pop	rdi
	pop	rsi
	pop	rbp
	pop	rbx
	ret	0
$LN8@write:

; 82   :         for (int i = 0; i < count; ++i)

	test	ebp, ebp
	jle	$LN6@write
	npad	14
$LL7@write:

; 83   :             a.setValueOn(coords[size_t(i) & mask], uint16_t(1 + ((i + phase) & 7)));

	lea	eax, DWORD PTR [r15+rdi]
; File D:\source\openvdb\openvdb\openvdb\TypeList.h

; 909  :         typelist_internal::TSEvalFirstPredImpl

	mov	QWORD PTR $T12[rsp], rbx
; File D:\SourceCode\Tedd.OpenVDB\benchmarks\NativeCacheTransfer\main.cpp

; 83   :             a.setValueOn(coords[size_t(i) & mask], uint16_t(1 + ((i + phase) & 7)));

	and	ax, 7
; File D:\source\openvdb\openvdb\openvdb\TypeList.h

; 531  :         if (pred(Idx)) op(std::get<Idx>(tup));

	xor	edx, edx
; File D:\SourceCode\Tedd.OpenVDB\benchmarks\NativeCacheTransfer\main.cpp

; 83   :             a.setValueOn(coords[size_t(i) & mask], uint16_t(1 + ((i + phase) & 7)));

	inc	ax
	mov	WORD PTR $T14[rsp], ax
	mov	eax, edi
	and	rax, r12
; File C:\Program Files\Microsoft Visual Studio\18\Insiders\VC\Tools\MSVC\14.51.36231\include\vector

; 1952 :         return _My_data._Myfirst[_Pos];

	lea	rcx, QWORD PTR [rax+rax*2]
	mov	rax, QWORD PTR [r14]
	lea	rsi, QWORD PTR [rax+rcx*4]
; File D:\source\openvdb\openvdb\openvdb\TypeList.h

; 531  :         if (pred(Idx)) op(std::get<Idx>(tup));

	lea	rcx, QWORD PTR $T12[rsp]

; 909  :         typelist_internal::TSEvalFirstPredImpl

	mov	QWORD PTR $T12[rsp+8], rsi

; 531  :         if (pred(Idx)) op(std::get<Idx>(tup));

	call	??$?RU?$integral_constant@_K$0A@@std@@@<lambda_ca3903ac405762ed18e3c24717acdf32>@@QEBA_NU?$integral_constant@_K$0A@@std@@@Z ; <lambda_ca3903ac405762ed18e3c24717acdf32>::operator()<std::integral_constant<unsigned __int64,0> >
	test	al, al
	je	SHORT $LN189@write
; File D:\Workspaces\AI\openvdb-native-cache-transfer-20261015\overlay\openvdb\tree\LeafNode.h

; 1072 :     return ((xyz[0] & (DIM-1u)) << 2*Log2Dim)

	mov	ecx, DWORD PTR [rsi]
	mov	eax, DWORD PTR [rsi+4]
	and	ecx, 7
; File D:\source\openvdb\openvdb\openvdb\TypeList.h

; 531  :         if (pred(Idx)) op(std::get<Idx>(tup));

	mov	r8, QWORD PTR [rbx+600]
; File D:\Workspaces\AI\openvdb-native-cache-transfer-20261015\overlay\openvdb\tree\LeafNode.h

; 1072 :     return ((xyz[0] & (DIM-1u)) << 2*Log2Dim)

	and	eax, 7
	lea	ecx, DWORD PTR [rax+rcx*8]
	mov	eax, DWORD PTR [rsi+8]
	and	eax, 7
	lea	edx, DWORD PTR [rax+rcx*8]
; File D:\Workspaces\AI\openvdb-native-cache-transfer-20261015\overlay\openvdb\tree\ValueAccessor.h

; 560  :                     const_cast<ValueType&>(LeafCacheT::buffer()[offset]) = value;

	mov	rcx, QWORD PTR [rbx+16]
	movzx	eax, WORD PTR $T14[rsp]
	mov	WORD PTR [rcx+rdx*2], ax

; 561  : #ifdef EXPERIMENT_DENSE_MASK
; 562  :                     // This experiment targets ordinary leaves with at least 64 voxels.
; 563  :                     // Smaller leaves retain their original mask API and behavior.
; 564  :                     if constexpr (NodeType::LOG2DIM >= 2) {
; 565  :                         if (node->getValueMask().template getWord<Index64>(offset >> 6) != ~Index64(0))

	mov	ecx, edx
	shr	rcx, 6
	mov	rax, QWORD PTR [r8+rcx*8+16]
	cmp	rax, -1
	je	$LN5@write
; File D:\source\openvdb\openvdb\openvdb\util\NodeMasks.h

; 455  :         mWords[n >> 6] |=  Word(1) << (n & 63);

	and	edx, 63					; 0000003fH
	bts	rax, rdx
	mov	QWORD PTR [r8+rcx*8+16], rax
; File D:\source\openvdb\openvdb\openvdb\TypeList.h

; 531  :         if (pred(Idx)) op(std::get<Idx>(tup));

	jmp	$LN5@write
$LN189@write:

; 532  :         else TSEvalFirstPredImpl<Pred, OpT, TupleT, Iter+1, End>(pred, op, tup);

	mov	r8, QWORD PTR $T12[rsp+8]
	mov	rcx, QWORD PTR $T12[rsp]
; File D:\source\openvdb\openvdb\openvdb\math\Coord.h

; 134  :     Int32 operator[](size_t i) const { OPENVDB_ASSERT(i < 3); return mVec[i]; }

	mov	r9d, DWORD PTR [r8]
; File D:\Workspaces\AI\openvdb-native-cache-transfer-20261015\overlay\openvdb\tree\ValueAccessor.h

; 968  :             const bool matches = (xyz[0] & ~Coord::ValueType(NodeT::DIM-1)) == mKeys[Idx][0]

	mov	eax, r9d
	and	eax, -128				; ffffffffffffff80H
	cmp	eax, DWORD PTR [rcx+548]
	jne	SHORT $LN219@write
	mov	eax, DWORD PTR [r8+4]
	and	eax, -128				; ffffffffffffff80H
	cmp	eax, DWORD PTR [rcx+552]
	jne	SHORT $LN219@write
	mov	eax, DWORD PTR [r8+8]
	and	eax, -128				; ffffffffffffff80H
	cmp	eax, DWORD PTR [rcx+556]
	jne	SHORT $LN219@write

; 575  :                     const_cast<NodeType*>(node)->setValueAndCache(xyz, value, *this);

	mov	rcx, QWORD PTR [rbx+592]
	lea	r8, QWORD PTR $T14[rsp]
	mov	r9, rbx
	mov	rdx, rsi
	call	??$setValueAndCache@V?$ValueAccessorImpl@V?$Tree@V?$RootNode@V?$InternalNode@V?$InternalNode@V?$LeafNode@G$02@tree@v13_0@openvdb@@$03@tree@v13_0@openvdb@@$04@tree@v13_0@openvdb@@@tree@v13_0@openvdb@@@tree@v13_0@openvdb@@$00XU?$index_sequence@$0A@$00$01@34@@tree@v13_0@openvdb@@@?$InternalNode@V?$LeafNode@G$02@tree@v13_0@openvdb@@$03@tree@v13_0@openvdb@@QEAAXAEBVCoord@math@23@AEBGAEAV?$ValueAccessorImpl@V?$Tree@V?$RootNode@V?$InternalNode@V?$InternalNode@V?$LeafNode@G$02@tree@v13_0@openvdb@@$03@tree@v13_0@openvdb@@$04@tree@v13_0@openvdb@@@tree@v13_0@openvdb@@@tree@v13_0@openvdb@@$00XU?$index_sequence@$0A@$00$01@34@@123@@Z ; openvdb::v13_0::tree::InternalNode<openvdb::v13_0::tree::LeafNode<unsigned short,3>,4>::setValueAndCache<openvdb::v13_0::tree::ValueAccessorImpl<openvdb::v13_0::tree::Tree<openvdb::v13_0::tree::RootNode<openvdb::v13_0::tree::InternalNode<openvdb::v13_0::tree::InternalNode<openvdb::v13_0::tree::LeafNode<unsigned short,3>,4>,5> > >,1,void,openvdb::v13_0::index_sequence<0,1,2> > >
; File D:\source\openvdb\openvdb\openvdb\TypeList.h

; 531  :         if (pred(Idx)) op(std::get<Idx>(tup));

	jmp	SHORT $LN5@write
$LN219@write:
; File D:\Workspaces\AI\openvdb-native-cache-transfer-20261015\overlay\openvdb\tree\ValueAccessor.h

; 968  :             const bool matches = (xyz[0] & ~Coord::ValueType(NodeT::DIM-1)) == mKeys[Idx][0]

	and	r9d, -4096				; fffffffffffff000H
	cmp	r9d, DWORD PTR [rcx+560]
	jne	SHORT $LN271@write
	mov	eax, DWORD PTR [r8+4]
	and	eax, -4096				; fffffffffffff000H
	cmp	eax, DWORD PTR [rcx+564]
	jne	SHORT $LN271@write
	mov	eax, DWORD PTR [r8+8]
	and	eax, -4096				; fffffffffffff000H
	cmp	eax, DWORD PTR [rcx+568]
	jne	SHORT $LN271@write

; 575  :                     const_cast<NodeType*>(node)->setValueAndCache(xyz, value, *this);

	mov	rcx, QWORD PTR [rbx+584]
	lea	r8, QWORD PTR $T14[rsp]
	mov	r9, rbx
	mov	rdx, rsi
	call	??$setValueAndCache@V?$ValueAccessorImpl@V?$Tree@V?$RootNode@V?$InternalNode@V?$InternalNode@V?$LeafNode@G$02@tree@v13_0@openvdb@@$03@tree@v13_0@openvdb@@$04@tree@v13_0@openvdb@@@tree@v13_0@openvdb@@@tree@v13_0@openvdb@@$00XU?$index_sequence@$0A@$00$01@34@@tree@v13_0@openvdb@@@?$InternalNode@V?$InternalNode@V?$LeafNode@G$02@tree@v13_0@openvdb@@$03@tree@v13_0@openvdb@@$04@tree@v13_0@openvdb@@QEAAXAEBVCoord@math@23@AEBGAEAV?$ValueAccessorImpl@V?$Tree@V?$RootNode@V?$InternalNode@V?$InternalNode@V?$LeafNode@G$02@tree@v13_0@openvdb@@$03@tree@v13_0@openvdb@@$04@tree@v13_0@openvdb@@@tree@v13_0@openvdb@@@tree@v13_0@openvdb@@$00XU?$index_sequence@$0A@$00$01@34@@123@@Z ; openvdb::v13_0::tree::InternalNode<openvdb::v13_0::tree::InternalNode<openvdb::v13_0::tree::LeafNode<unsigned short,3>,4>,5>::setValueAndCache<openvdb::v13_0::tree::ValueAccessorImpl<openvdb::v13_0::tree::Tree<openvdb::v13_0::tree::RootNode<openvdb::v13_0::tree::InternalNode<openvdb::v13_0::tree::InternalNode<openvdb::v13_0::tree::LeafNode<unsigned short,3>,4>,5> > >,1,void,openvdb::v13_0::index_sequence<0,1,2> > >
; File D:\source\openvdb\openvdb\openvdb\TypeList.h

; 531  :         if (pred(Idx)) op(std::get<Idx>(tup));

	jmp	SHORT $LN5@write
$LN271@write:
; File D:\Workspaces\AI\openvdb-native-cache-transfer-20261015\overlay\openvdb\tree\ValueAccessor.h

; 575  :                     const_cast<NodeType*>(node)->setValueAndCache(xyz, value, *this);

	mov	rcx, QWORD PTR [rbx+576]
	lea	r8, QWORD PTR $T14[rsp]
	mov	r9, rbx
	mov	rdx, rsi
	call	??$setValueAndCache@V?$ValueAccessorImpl@V?$Tree@V?$RootNode@V?$InternalNode@V?$InternalNode@V?$LeafNode@G$02@tree@v13_0@openvdb@@$03@tree@v13_0@openvdb@@$04@tree@v13_0@openvdb@@@tree@v13_0@openvdb@@@tree@v13_0@openvdb@@$00XU?$index_sequence@$0A@$00$01@34@@tree@v13_0@openvdb@@@?$RootNode@V?$InternalNode@V?$InternalNode@V?$LeafNode@G$02@tree@v13_0@openvdb@@$03@tree@v13_0@openvdb@@$04@tree@v13_0@openvdb@@@tree@v13_0@openvdb@@QEAAXAEBVCoord@math@23@AEBGAEAV?$ValueAccessorImpl@V?$Tree@V?$RootNode@V?$InternalNode@V?$InternalNode@V?$LeafNode@G$02@tree@v13_0@openvdb@@$03@tree@v13_0@openvdb@@$04@tree@v13_0@openvdb@@@tree@v13_0@openvdb@@@tree@v13_0@openvdb@@$00XU?$index_sequence@$0A@$00$01@34@@123@@Z ; openvdb::v13_0::tree::RootNode<openvdb::v13_0::tree::InternalNode<openvdb::v13_0::tree::InternalNode<openvdb::v13_0::tree::LeafNode<unsigned short,3>,4>,5> >::setValueAndCache<openvdb::v13_0::tree::ValueAccessorImpl<openvdb::v13_0::tree::Tree<openvdb::v13_0::tree::RootNode<openvdb::v13_0::tree::InternalNode<openvdb::v13_0::tree::InternalNode<openvdb::v13_0::tree::LeafNode<unsigned short,3>,4>,5> > >,1,void,openvdb::v13_0::index_sequence<0,1,2> > >
$LN5@write:
; File D:\SourceCode\Tedd.OpenVDB\benchmarks\NativeCacheTransfer\main.cpp

; 82   :         for (int i = 0; i < count; ++i)

	inc	edi
	cmp	edi, ebp
	jl	$LL7@write
$LN6@write:

; 84   :     }
; 85   : }

	add	rsp, 48					; 00000030H
	pop	r15
	pop	r14
	pop	r12
	pop	rdi
	pop	rsi
	pop	rbp
	pop	rbx
	ret	0
?write@@YAXAEAV?$ValueAccessorImpl@V?$Tree@V?$RootNode@V?$InternalNode@V?$InternalNode@V?$LeafNode@G$02@tree@v13_0@openvdb@@$03@tree@v13_0@openvdb@@$04@tree@v13_0@openvdb@@@tree@v13_0@openvdb@@@tree@v13_0@openvdb@@$00XU?$index_sequence@$0A@$00$01@34@@tree@v13_0@openvdb@@AEBV?$vector@VCoord@math@v13_0@openvdb@@V?$allocator@VCoord@math@v13_0@openvdb@@@std@@@std@@HH_N@Z ENDP ; write
