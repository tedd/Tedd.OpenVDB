?read@@YA_KAEAV?$ValueAccessorImpl@V?$Tree@V?$RootNode@V?$InternalNode@V?$InternalNode@V?$LeafNode@G$02@tree@v13_0@openvdb@@$03@tree@v13_0@openvdb@@$04@tree@v13_0@openvdb@@@tree@v13_0@openvdb@@@tree@v13_0@openvdb@@$00XU?$index_sequence@$0A@$00$01@34@@tree@v13_0@openvdb@@AEBV?$vector@VCoord@math@v13_0@openvdb@@V?$allocator@VCoord@math@v13_0@openvdb@@@std@@@std@@H@Z PROC ; read, COMDAT

; 66   : {

$LN284:
	mov	QWORD PTR [rsp+32], rbx
	push	rbp
	push	r12
	push	r13
	push	r14
	push	r15
	sub	rsp, 48					; 00000030H
	mov	rbx, rcx
	mov	r12, rdx
; File C:\Program Files\Microsoft Visual Studio\18\Insiders\VC\Tools\MSVC\14.51.36231\include\vector

; 1922 :         return static_cast<size_type>(_My_data._Mylast - _My_data._Myfirst);

	mov	rcx, QWORD PTR [rdx+8]
	mov	rax, 3074457345618258603		; 2aaaaaaaaaaaaaabH
	sub	rcx, QWORD PTR [rdx]
; File D:\SourceCode\Tedd.OpenVDB\benchmarks\NativeCacheTransfer\main.cpp

; 67   :     uint64_t sum = 0;

	xor	r14d, r14d
; File C:\Program Files\Microsoft Visual Studio\18\Insiders\VC\Tools\MSVC\14.51.36231\include\vector

; 1922 :         return static_cast<size_type>(_My_data._Mylast - _My_data._Myfirst);

	imul	rcx
; File D:\SourceCode\Tedd.OpenVDB\benchmarks\NativeCacheTransfer\main.cpp

; 66   : {

	xor	ebp, ebp
	mov	r15d, r8d
; File C:\Program Files\Microsoft Visual Studio\18\Insiders\VC\Tools\MSVC\14.51.36231\include\vector

; 1922 :         return static_cast<size_type>(_My_data._Mylast - _My_data._Myfirst);

	sar	rdx, 1
	mov	r13, rdx
	shr	r13, 63					; 0000003fH
; File D:\SourceCode\Tedd.OpenVDB\benchmarks\NativeCacheTransfer\main.cpp

; 68   :     const size_t mask = coords.size() - 1;

	dec	r13
	add	r13, rdx

; 69   :     for (int i = 0; i < count; ++i) sum += a.getValue(coords[size_t(i) & mask]);

	test	r8d, r8d
	jle	$LN268@read
	mov	QWORD PTR [rsp+112], rdi
	movaps	XMMWORD PTR [rsp+32], xmm6
	movdqa	xmm6, XMMWORD PTR __xmm@fffff000fffff000fffff000fffff000
	mov	QWORD PTR [rsp+96], rsi
	npad	12
$LL4@read:
	mov	eax, ebp
	and	rax, r13
; File C:\Program Files\Microsoft Visual Studio\18\Insiders\VC\Tools\MSVC\14.51.36231\include\vector

; 1952 :         return _My_data._Myfirst[_Pos];

	lea	rcx, QWORD PTR [rax+rax*2]
	mov	rax, QWORD PTR [r12]
; File D:\Workspaces\AI\openvdb-native-cache-transfer-20261015\overlay\openvdb\tree\ValueAccessor.h

; 968  :             const bool matches = (xyz[0] & ~Coord::ValueType(NodeT::DIM-1)) == mKeys[Idx][0]

	mov	edx, DWORD PTR [rax+rcx*4]
; File C:\Program Files\Microsoft Visual Studio\18\Insiders\VC\Tools\MSVC\14.51.36231\include\vector

; 1952 :         return _My_data._Myfirst[_Pos];

	lea	rdi, QWORD PTR [rax+rcx*4]
; File D:\Workspaces\AI\openvdb-native-cache-transfer-20261015\overlay\openvdb\tree\ValueAccessor.h

; 968  :             const bool matches = (xyz[0] & ~Coord::ValueType(NodeT::DIM-1)) == mKeys[Idx][0]

	mov	eax, edx
	and	eax, -8
	cmp	eax, DWORD PTR [rbx+24]
	jne	SHORT $LN266@read
	mov	ecx, DWORD PTR [rdi+4]
	mov	eax, ecx
	and	eax, -8
	cmp	eax, DWORD PTR [rbx+28]
	jne	SHORT $LN266@read
	mov	r8d, DWORD PTR [rdi+8]
	mov	eax, r8d
	and	eax, -8
	cmp	eax, DWORD PTR [rbx+32]
	jne	SHORT $LN266@read
; File D:\Workspaces\AI\openvdb-native-cache-transfer-20261015\overlay\openvdb\tree\LeafNode.h

; 1072 :     return ((xyz[0] & (DIM-1u)) << 2*Log2Dim)

	and	ecx, 7
	mov	eax, edx
	and	eax, 7
	and	r8d, 7
	shl	eax, 3
	or	ecx, eax
; File D:\Workspaces\AI\openvdb-native-cache-transfer-20261015\overlay\openvdb\tree\ValueAccessor.h

; 468  :                     return &(LeafCacheT::buffer()[LeafNodeT::coordToOffset(xyz)]);

	mov	rax, QWORD PTR [rbx+16]
; File D:\Workspaces\AI\openvdb-native-cache-transfer-20261015\overlay\openvdb\tree\LeafNode.h

; 1072 :     return ((xyz[0] & (DIM-1u)) << 2*Log2Dim)

	shl	ecx, 3
	or	ecx, r8d
; File D:\Workspaces\AI\openvdb-native-cache-transfer-20261015\overlay\openvdb\tree\ValueAccessor.h

; 468  :                     return &(LeafCacheT::buffer()[LeafNodeT::coordToOffset(xyz)]);

	lea	rax, QWORD PTR [rax+rcx*2]
; File D:\source\openvdb\openvdb\openvdb\TypeList.h

; 516  :         if (auto ret = op(std::integral_constant<std::size_t, Iter>())) return ret;

	test	rax, rax
	jne	$LN190@read
$LN266@read:
; File D:\Workspaces\AI\openvdb-native-cache-transfer-20261015\overlay\openvdb\tree\ValueAccessor.h

; 968  :             const bool matches = (xyz[0] & ~Coord::ValueType(NodeT::DIM-1)) == mKeys[Idx][0]

	and	edx, -128				; ffffffffffffff80H
	cmp	edx, DWORD PTR [rbx+36]
	jne	SHORT $LN87@read
	mov	eax, DWORD PTR [rdi+4]
	and	eax, -128				; ffffffffffffff80H
	cmp	eax, DWORD PTR [rbx+40]
	jne	SHORT $LN87@read
	mov	eax, DWORD PTR [rdi+8]
	and	eax, -128				; ffffffffffffff80H
	cmp	eax, DWORD PTR [rbx+44]
	jne	SHORT $LN87@read

; 473  :                     return &(node->getValueAndCache(xyz, *this));

	mov	rcx, QWORD PTR [rbx+80]
	mov	r8, rbx
	mov	rdx, rdi
	call	??$getValueAndCache@$$CBV?$ValueAccessorImpl@V?$Tree@V?$RootNode@V?$InternalNode@V?$InternalNode@V?$LeafNode@G$02@tree@v13_0@openvdb@@$03@tree@v13_0@openvdb@@$04@tree@v13_0@openvdb@@@tree@v13_0@openvdb@@@tree@v13_0@openvdb@@$00XU?$index_sequence@$0A@$00$01@34@@tree@v13_0@openvdb@@@?$InternalNode@V?$LeafNode@G$02@tree@v13_0@openvdb@@$03@tree@v13_0@openvdb@@QEBAAEBGAEBVCoord@math@23@AEBV?$ValueAccessorImpl@V?$Tree@V?$RootNode@V?$InternalNode@V?$InternalNode@V?$LeafNode@G$02@tree@v13_0@openvdb@@$03@tree@v13_0@openvdb@@$04@tree@v13_0@openvdb@@@tree@v13_0@openvdb@@@tree@v13_0@openvdb@@$00XU?$index_sequence@$0A@$00$01@34@@123@@Z ; openvdb::v13_0::tree::InternalNode<openvdb::v13_0::tree::LeafNode<unsigned short,3>,4>::getValueAndCache<openvdb::v13_0::tree::ValueAccessorImpl<openvdb::v13_0::tree::Tree<openvdb::v13_0::tree::RootNode<openvdb::v13_0::tree::InternalNode<openvdb::v13_0::tree::InternalNode<openvdb::v13_0::tree::LeafNode<unsigned short,3>,4>,5> > >,1,void,openvdb::v13_0::index_sequence<0,1,2> > const >
; File D:\source\openvdb\openvdb\openvdb\TypeList.h

; 516  :         if (auto ret = op(std::integral_constant<std::size_t, Iter>())) return ret;

	test	rax, rax
	jne	$LN190@read
$LN87@read:
; File D:\Workspaces\AI\openvdb-native-cache-transfer-20261015\overlay\openvdb\tree\ValueAccessor.h

; 968  :             const bool matches = (xyz[0] & ~Coord::ValueType(NodeT::DIM-1)) == mKeys[Idx][0]

	mov	eax, DWORD PTR [rdi]
	and	eax, -4096				; fffffffffffff000H
	cmp	eax, DWORD PTR [rbx+48]
	jne	SHORT $LN143@read
	mov	eax, DWORD PTR [rdi+4]
	and	eax, -4096				; fffffffffffff000H
	cmp	eax, DWORD PTR [rbx+52]
	jne	SHORT $LN143@read
	mov	eax, DWORD PTR [rdi+8]
	and	eax, -4096				; fffffffffffff000H
	cmp	eax, DWORD PTR [rbx+56]
	jne	SHORT $LN143@read

; 473  :                     return &(node->getValueAndCache(xyz, *this));

	mov	rcx, QWORD PTR [rbx+72]
	mov	r8, rbx
	mov	rdx, rdi
	call	??$getValueAndCache@$$CBV?$ValueAccessorImpl@V?$Tree@V?$RootNode@V?$InternalNode@V?$InternalNode@V?$LeafNode@G$02@tree@v13_0@openvdb@@$03@tree@v13_0@openvdb@@$04@tree@v13_0@openvdb@@@tree@v13_0@openvdb@@@tree@v13_0@openvdb@@$00XU?$index_sequence@$0A@$00$01@34@@tree@v13_0@openvdb@@@?$InternalNode@V?$InternalNode@V?$LeafNode@G$02@tree@v13_0@openvdb@@$03@tree@v13_0@openvdb@@$04@tree@v13_0@openvdb@@QEBAAEBGAEBVCoord@math@23@AEBV?$ValueAccessorImpl@V?$Tree@V?$RootNode@V?$InternalNode@V?$InternalNode@V?$LeafNode@G$02@tree@v13_0@openvdb@@$03@tree@v13_0@openvdb@@$04@tree@v13_0@openvdb@@@tree@v13_0@openvdb@@@tree@v13_0@openvdb@@$00XU?$index_sequence@$0A@$00$01@34@@123@@Z ; openvdb::v13_0::tree::InternalNode<openvdb::v13_0::tree::InternalNode<openvdb::v13_0::tree::LeafNode<unsigned short,3>,4>,5>::getValueAndCache<openvdb::v13_0::tree::ValueAccessorImpl<openvdb::v13_0::tree::Tree<openvdb::v13_0::tree::RootNode<openvdb::v13_0::tree::InternalNode<openvdb::v13_0::tree::InternalNode<openvdb::v13_0::tree::LeafNode<unsigned short,3>,4>,5> > >,1,void,openvdb::v13_0::index_sequence<0,1,2> > const >
; File D:\source\openvdb\openvdb\openvdb\TypeList.h

; 516  :         if (auto ret = op(std::integral_constant<std::size_t, Iter>())) return ret;

	test	rax, rax
	jne	SHORT $LN190@read
$LN143@read:
; File D:\Workspaces\AI\openvdb-native-cache-transfer-20261015\overlay\openvdb\tree\ValueAccessor.h

; 471  :                     auto node = mNodes.template get<Idx>();

	mov	rsi, QWORD PTR [rbx+64]
; File D:\Workspaces\AI\openvdb-native-cache-transfer-20261015\overlay\openvdb\tree\RootNode.h

; 1771 :     MapCIter iter = this->findCoord(xyz);

	lea	rdx, QWORD PTR iter$4[rsp]
	mov	rcx, rsi
	mov	r8, rdi
	call	?findCoord@?$RootNode@V?$InternalNode@V?$InternalNode@V?$LeafNode@G$02@tree@v13_0@openvdb@@$03@tree@v13_0@openvdb@@$04@tree@v13_0@openvdb@@@tree@v13_0@openvdb@@AEBA?AV?$_Tree_const_iterator@V?$_Tree_val@U?$_Tree_simple_types@U?$pair@$$CBVCoord@math@v13_0@openvdb@@UNodeStruct@?$RootNode@V?$InternalNode@V?$InternalNode@V?$LeafNode@G$02@tree@v13_0@openvdb@@$03@tree@v13_0@openvdb@@$04@tree@v13_0@openvdb@@@tree@34@@std@@@std@@@std@@@std@@AEBVCoord@math@34@@Z ; openvdb::v13_0::tree::RootNode<openvdb::v13_0::tree::InternalNode<openvdb::v13_0::tree::InternalNode<openvdb::v13_0::tree::LeafNode<unsigned short,3>,4>,5> >::findCoord

; 1772 :     if (iter == mTable.end()) return mBackground;

	mov	rax, QWORD PTR iter$4[rsp]
	cmp	rax, QWORD PTR [rsi]
	jne	SHORT $LN191@read
	lea	rax, QWORD PTR [rsi+16]
	jmp	SHORT $LN190@read
$LN191@read:

; 147  :         bool isChild() const { return child != nullptr; }

	mov	rdx, QWORD PTR [rax+48]

; 1773 :     if (isChild(iter)) {

	test	rdx, rdx
	je	SHORT $LN192@read
; File D:\source\openvdb\openvdb\openvdb\math\Coord.h

; 126  :     Coord  operator&  (Int32 n) const { return Coord(mVec[0] & n, mVec[1] & n, mVec[2] & n); }

	mov	ecx, DWORD PTR [rdi+8]
; File D:\Workspaces\AI\openvdb-native-cache-transfer-20261015\overlay\openvdb\tree\RootNode.h

; 1775 :         return getChild(iter).getValueAndCache(xyz, acc);

	mov	r8, rbx
; File D:\Workspaces\AI\openvdb-native-cache-transfer-20261015\overlay\openvdb\tree\ValueAccessor.h

; 945  :             mKeys[Idx] = xyz & ~(NodeT::DIM-1);

	movq	xmm0, QWORD PTR [rdi]
; File D:\source\openvdb\openvdb\openvdb\math\Coord.h

; 126  :     Coord  operator&  (Int32 n) const { return Coord(mVec[0] & n, mVec[1] & n, mVec[2] & n); }

	and	ecx, -4096				; fffffffffffff000H
; File D:\Workspaces\AI\openvdb-native-cache-transfer-20261015\overlay\openvdb\tree\ValueAccessor.h

; 945  :             mKeys[Idx] = xyz & ~(NodeT::DIM-1);

	andps	xmm0, xmm6
	movq	QWORD PTR [rbx+48], xmm0
	mov	DWORD PTR [rbx+56], ecx

; 946  :             mNodes.template get<Idx>() = const_cast<NodeT*>(node);

	mov	QWORD PTR [rbx+72], rdx
; File D:\Workspaces\AI\openvdb-native-cache-transfer-20261015\overlay\openvdb\tree\RootNode.h

; 1775 :         return getChild(iter).getValueAndCache(xyz, acc);

	mov	rdx, rdi
	mov	rcx, QWORD PTR [rax+48]
	call	??$getValueAndCache@$$CBV?$ValueAccessorImpl@V?$Tree@V?$RootNode@V?$InternalNode@V?$InternalNode@V?$LeafNode@G$02@tree@v13_0@openvdb@@$03@tree@v13_0@openvdb@@$04@tree@v13_0@openvdb@@@tree@v13_0@openvdb@@@tree@v13_0@openvdb@@$00XU?$index_sequence@$0A@$00$01@34@@tree@v13_0@openvdb@@@?$InternalNode@V?$InternalNode@V?$LeafNode@G$02@tree@v13_0@openvdb@@$03@tree@v13_0@openvdb@@$04@tree@v13_0@openvdb@@QEBAAEBGAEBVCoord@math@23@AEBV?$ValueAccessorImpl@V?$Tree@V?$RootNode@V?$InternalNode@V?$InternalNode@V?$LeafNode@G$02@tree@v13_0@openvdb@@$03@tree@v13_0@openvdb@@$04@tree@v13_0@openvdb@@@tree@v13_0@openvdb@@@tree@v13_0@openvdb@@$00XU?$index_sequence@$0A@$00$01@34@@123@@Z ; openvdb::v13_0::tree::InternalNode<openvdb::v13_0::tree::InternalNode<openvdb::v13_0::tree::LeafNode<unsigned short,3>,4>,5>::getValueAndCache<openvdb::v13_0::tree::ValueAccessorImpl<openvdb::v13_0::tree::Tree<openvdb::v13_0::tree::RootNode<openvdb::v13_0::tree::InternalNode<openvdb::v13_0::tree::InternalNode<openvdb::v13_0::tree::LeafNode<unsigned short,3>,4>,5> > >,1,void,openvdb::v13_0::index_sequence<0,1,2> > const >
	jmp	SHORT $LN190@read
$LN192@read:

; 168  :     static const Tile&      getTile(const MapCIter& i) { return i->second.tile; }

	add	rax, 56					; 00000038H
$LN190@read:
; File D:\SourceCode\Tedd.OpenVDB\benchmarks\NativeCacheTransfer\main.cpp

; 69   :     for (int i = 0; i < count; ++i) sum += a.getValue(coords[size_t(i) & mask]);

	movzx	ecx, WORD PTR [rax]
	inc	ebp
	add	r14, rcx
	cmp	ebp, r15d
	jl	$LL4@read
	movaps	xmm6, XMMWORD PTR [rsp+32]
	mov	rdi, QWORD PTR [rsp+112]
	mov	rsi, QWORD PTR [rsp+96]
$LN268@read:

; 70   :     return sum;
; 71   : }

	mov	rbx, QWORD PTR [rsp+120]
	mov	rax, r14
	add	rsp, 48					; 00000030H
	pop	r15
	pop	r14
	pop	r13
	pop	r12
	pop	rbp
	ret	0
?read@@YA_KAEAV?$ValueAccessorImpl@V?$Tree@V?$RootNode@V?$InternalNode@V?$InternalNode@V?$LeafNode@G$02@tree@v13_0@openvdb@@$03@tree@v13_0@openvdb@@$04@tree@v13_0@openvdb@@@tree@v13_0@openvdb@@@tree@v13_0@openvdb@@$00XU?$index_sequence@$0A@$00$01@34@@tree@v13_0@openvdb@@AEBV?$vector@VCoord@math@v13_0@openvdb@@V?$allocator@VCoord@math@v13_0@openvdb@@@std@@@std@@H@Z ENDP ; read
